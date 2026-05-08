using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using OrganizeContacts.Core.Merge;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.App.Views;

public partial class MergeReviewDialog : Window
{
    private const string EmptyPlaceholder = "(empty)";

    public ObservableCollection<MergeFieldRow> Rows { get; } = new();
    public Contact Primary { get; }

    /// <summary>The contact shown in the right column. For N-way groups this is the
    /// first secondary; the remaining secondaries are still merged into the survivor
    /// via the <see cref="MergePlan"/> so collections are unioned across all members.</summary>
    public Contact Secondary { get; }

    /// <summary>All members other than the primary. Always merged into the survivor.</summary>
    public IReadOnlyList<Contact> AllSecondaries { get; }

    public bool DeleteSecondaries { get; private set; } = true;
    public MergePlan? Result { get; private set; }

    public MergeReviewDialog(DuplicateGroup group)
    {
        InitializeComponent();
        if (group.Members.Count < 2)
            throw new InvalidOperationException("MergeReviewDialog requires a group with at least two members.");

        Primary = group.Members[0];
        AllSecondaries = group.Members.Skip(1).ToList();
        Secondary = AllSecondaries[0];

        var signals = string.Join(" · ", group.Signals.Select(s => $"{s.Label} (+{s.Weight:0.00})"));
        var memberNote = AllSecondaries.Count > 1
            ? $"  ·  {AllSecondaries.Count} secondary contacts will be merged (collections unioned across all)"
            : string.Empty;
        ConfidenceText.Text = $"Confidence {group.Confidence:P0}.  {signals}{memberNote}";

        // Pick scalar values from "the first secondary that has it" so a 3+ way merge still
        // picks up data from member #3 if member #2 is missing the field.
        AddRow("FormattedName", "Formatted name", Primary.FormattedName, FirstNonEmpty(c => c.FormattedName));
        AddRow("GivenName", "Given name", Primary.GivenName, FirstNonEmpty(c => c.GivenName));
        AddRow("FamilyName", "Family name", Primary.FamilyName, FirstNonEmpty(c => c.FamilyName));
        AddRow("AdditionalNames", "Additional names", Primary.AdditionalNames, FirstNonEmpty(c => c.AdditionalNames));
        AddRow("HonorificPrefix", "Prefix", Primary.HonorificPrefix, FirstNonEmpty(c => c.HonorificPrefix));
        AddRow("HonorificSuffix", "Suffix", Primary.HonorificSuffix, FirstNonEmpty(c => c.HonorificSuffix));
        AddRow("Nickname", "Nickname", Primary.Nickname, FirstNonEmpty(c => c.Nickname));
        AddRow("Organization", "Organization", Primary.Organization, FirstNonEmpty(c => c.Organization));
        AddRow("Title", "Title", Primary.Title, FirstNonEmpty(c => c.Title));
        AddRow("Birthday", "Birthday",
            Primary.Birthday?.ToString("yyyy-MM-dd"),
            AllSecondaries.Select(c => c.Birthday?.ToString("yyyy-MM-dd"))
                          .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)));
        AddRow("Anniversary", "Anniversary",
            Primary.Anniversary?.ToString("yyyy-MM-dd"),
            AllSecondaries.Select(c => c.Anniversary?.ToString("yyyy-MM-dd"))
                          .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)));
        AddRow("Notes", "Notes", Primary.Notes, FirstNonEmpty(c => c.Notes));

        FieldList.ItemsSource = Rows;
    }

    private string? FirstNonEmpty(Func<Contact, string?> selector) =>
        AllSecondaries.Select(selector).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private void AddRow(string field, string label, string? left, string? right)
    {
        var row = new MergeFieldRow
        {
            FieldName = field,
            FieldLabel = label,
            LeftValue = string.IsNullOrEmpty(left) ? EmptyPlaceholder : left,
            RightValue = string.IsNullOrEmpty(right) ? EmptyPlaceholder : right,
        };
        // Default: keep primary unless primary is empty AND a secondary has a value.
        if (string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right))
            row.ChooseRight = true;
        else
            row.ChooseLeft = true;
        Rows.Add(row);
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        DeleteSecondaries = DeleteSecondariesBox.IsChecked == true;

        var choices = Rows.Select(r =>
        {
            var picked = r.ChooseLeft ? r.LeftValue : r.RightValue;
            // Translate the placeholder back to a real null instead of writing the literal
            // string "(empty)" into the survivor.
            string? value = picked == EmptyPlaceholder ? null : picked;
            return new MergeChoice(
                r.FieldName,
                r.ChooseLeft ? MergeFieldOrigin.Primary : MergeFieldOrigin.Secondary,
                value);
        }).ToList();

        Result = new MergePlan
        {
            Primary = Primary,
            // Pass ALL secondaries — MergeEngine will union phones/emails/addresses/urls/categories
            // across every secondary so a 3+ contact group merges in one pass.
            Secondaries = AllSecondaries.ToList(),
            Choices = choices,
            DeleteSecondaries = DeleteSecondaries,
        };
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed class MergeFieldRow : INotifyPropertyChanged
{
    private bool _chooseLeft;
    private bool _chooseRight;

    public string FieldName { get; init; } = string.Empty;
    public string FieldLabel { get; init; } = string.Empty;
    public string LeftValue { get; init; } = string.Empty;
    public string RightValue { get; init; } = string.Empty;

    public bool ChooseLeft
    {
        get => _chooseLeft;
        set { _chooseLeft = value; OnPropertyChanged(); if (value) ChooseRight = false; }
    }

    public bool ChooseRight
    {
        get => _chooseRight;
        set { _chooseRight = value; OnPropertyChanged(); if (value) ChooseLeft = false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
