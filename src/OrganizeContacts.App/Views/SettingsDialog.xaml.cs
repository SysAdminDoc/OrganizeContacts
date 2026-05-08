using System.Windows;
using System.Windows.Controls;
using OrganizeContacts.Core;

namespace OrganizeContacts.App.Views;

public partial class SettingsDialog : Window
{
    public AppSettings Working { get; }

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        Working = settings;
        RegionBox.Text = settings.DefaultRegion;
        foreach (ComboBoxItem item in ProfileCombo.Items)
            if (string.Equals((string?)item.Content, settings.MatchProfile, System.StringComparison.OrdinalIgnoreCase))
                item.IsSelected = true;
        if (ProfileCombo.SelectedIndex < 0) ProfileCombo.SelectedIndex = 0;
        MergeGoogleMailBox.IsChecked = settings.MergeGoogleMailDomain;
        StripGmailDotsBox.IsChecked = settings.StripGmailDots;
        StripPlusTagBox.IsChecked = settings.StripPlusTag;
        ConfirmDestructiveBox.IsChecked = settings.ConfirmDestructiveActions;
        ThemeCombo.SelectedIndex = string.Equals(settings.Theme, "Latte", System.StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Working.DefaultRegion = string.IsNullOrWhiteSpace(RegionBox.Text) ? "US" : RegionBox.Text.Trim().ToUpperInvariant();
        Working.MatchProfile = (string?)((ComboBoxItem?)ProfileCombo.SelectedItem)?.Content ?? "Default";
        Working.MergeGoogleMailDomain = MergeGoogleMailBox.IsChecked == true;
        Working.StripGmailDots = StripGmailDotsBox.IsChecked == true;
        Working.StripPlusTag = StripPlusTagBox.IsChecked == true;
        Working.ConfirmDestructiveActions = ConfirmDestructiveBox.IsChecked == true;
        Working.Theme = ThemeCombo.SelectedIndex == 1 ? "Latte" : "Mocha";
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
