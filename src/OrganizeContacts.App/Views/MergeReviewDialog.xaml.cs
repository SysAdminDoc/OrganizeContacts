using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using OrganizeContacts.Core.Merge;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.App.Views;

public partial class MergeReviewDialog : Window
{
    public ObservableCollection<MergeFieldRow> Rows { get; } = new();
    public Contact Primary { get; }
    public Contact Secondary { get; }
    public bool DeleteSecondaries { get; private set; } = true;

    public MergePlan? Result { get; private set; }

    public MergeReviewDialog(DuplicateGroup group)
    {
        InitializeComponent();
        if (group.Members.Count < 2)
            throw new InvalidOperationException("MergeReviewDialog requires a group with at least two members.");

        Primary = group.Members[0];
        Secondary = group.Members[1];

        var signals = string.Join(" · ", group.Signals.Select(s => $"{s.Label} (+{s.Weight:0.00})"));
        ConfidenceText.Text =
            $"Confidence {group.Confidence:P0}.  {signals}";

        AddRow("FormattedName", "Formatted name", Primary.FormattedName, Secondary.FormattedName);
        AddRow("GivenName", "Given name", Primary.GivenName, Secondary.GivenName);
        AddRow("FamilyName", "Family name", Primary.FamilyName, Secondary.FamilyName);
        AddRow("Nickname", "Nickname", Primary.Nickname, Secondary.Nickname);
        AddRow("Organization", "Organization", Primary.Organization, Secondary.Organization);
        AddRow("Title", "Title", Primary.Title, Secondary.Title);
        AddRow("Birthday", "Birthday",
            Primary.Birthday?.ToString("yyyy-MM-dd"), Secondary.Birthday?.ToString("yyyy-MM-dd"));
        AddRow("Anniversary", "Anniversary",
            Primary.Anniversary?.ToString("yyyy-MM-dd"), Secondary.Anniversary?.ToString("yyyy-MM-dd"));
        AddRow("Notes", "Notes", Primary.Notes, Secondary.Notes);

        FieldList.ItemsSource = Rows;
    }

    private void AddRow(string field, string label, string? left, string? right)
    {
        var row = new MergeFieldRow
        {
            FieldName = field,
            FieldLabel = label,
            LeftValue = left ?? "(empty)",
            RightValue = right ?? "(empty)",
        };
        // Default: keep primary unless primary is empty.
        if (string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right))
            row.ChooseRight = true;
        else
            row.ChooseLeft = true;
        Rows.Add(row);
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        DeleteSecondaries = DeleteSecondariesBox.IsChecked == true;

        var choices = Rows.Select(r => new MergeChoice(
            r.FieldName,
            r.ChooseLeft ? MergeFieldOrigin.Primary : MergeFieldOrigin.Secondary,
            (r.ChooseLeft ? r.LeftValue : r.RightValue) == "(empty)"
                ? null
                : (r.ChooseLeft ? r.LeftValue : r.RightValue))).ToList();

        Result = new MergePlan
        {
            Primary = Primary,
            Secondaries = new List<Contact> { Secondary },
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
