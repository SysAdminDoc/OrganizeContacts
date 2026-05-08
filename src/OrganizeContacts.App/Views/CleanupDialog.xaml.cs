using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using OrganizeContacts.Core.Cleanup;

namespace OrganizeContacts.App.Views;

public partial class CleanupDialog : Window
{
    public bool DedupePhones => DedupePhonesBox.IsChecked == true;
    public bool DedupeEmails => DedupeEmailsBox.IsChecked == true;
    public bool DedupeUrls => DedupeUrlsBox.IsChecked == true;
    public bool DedupeCategories => DedupeCategoriesBox.IsChecked == true;
    public bool NormalizePhones => NormalizePhonesBox.IsChecked == true;
    public bool CanonicalizeEmails => CanonicalizeEmailsBox.IsChecked == true;
    public RegexEdit? Regex { get; private set; }

    public CleanupDialog() => InitializeComponent();

    private void OnRun(object sender, RoutedEventArgs e)
    {
        var item = (ComboBoxItem?)RegexTargetCombo.SelectedItem;
        var label = (string?)item?.Content ?? "(none)";
        if (label != "(none)" && !string.IsNullOrWhiteSpace(RegexPatternBox.Text))
        {
            try
            {
                var opts = RegexIgnoreCaseBox.IsChecked == true ? RegexOptions.IgnoreCase : RegexOptions.None;
                var target = (RegexTarget)System.Enum.Parse(typeof(RegexTarget), label);
                Regex = new RegexEdit(target, RegexPatternBox.Text, RegexReplacementBox.Text ?? "", opts);
                _ = new Regex(RegexPatternBox.Text, opts); // syntax check
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, $"Invalid regex: {ex.Message}", "Cleanup",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
