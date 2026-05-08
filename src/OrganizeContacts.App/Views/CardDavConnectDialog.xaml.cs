using System.Windows;
using OrganizeContacts.Core.CardDav;

namespace OrganizeContacts.App.Views;

public partial class CardDavConnectDialog : Window
{
    public string ServerUrl => UrlBox.Text.Trim();
    public string Username => UserBox.Text.Trim();
    public string Password => PassBox.Password;
    public bool SaveCredentials => SaveCredsBox.IsChecked == true;
    public AddressBookInfo? SelectedBook => BooksList.SelectedItem as AddressBookInfo;

    public CardDavConnectDialog(string? prefilledUrl = null, string? prefilledUser = null, string? prefilledPass = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(prefilledUrl)) UrlBox.Text = prefilledUrl;
        if (!string.IsNullOrWhiteSpace(prefilledUser)) UserBox.Text = prefilledUser;
        if (!string.IsNullOrWhiteSpace(prefilledPass)) PassBox.Password = prefilledPass;
    }

    private async void OnDiscover(object sender, RoutedEventArgs e)
    {
        try
        {
            var client = new CardDavClient(new System.Uri(ServerUrl), Username, Password);
            var books = await client.DiscoverAddressBooksAsync();
            BooksList.ItemsSource = books;
            if (books.Count == 0)
                MessageBox.Show(this, "No address books found.", "CardDAV", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this, $"Discovery failed: {ex.Message}", "CardDAV",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        if (SelectedBook is null)
        {
            MessageBox.Show(this, "Pick an address book first.", "CardDAV",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
