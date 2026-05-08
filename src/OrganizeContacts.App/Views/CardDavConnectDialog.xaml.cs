using System.Windows;
using System.Windows.Input;
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

    private bool _discovering;

    private async void OnDiscover(object sender, RoutedEventArgs e)
    {
        // Block re-entry — multiple clicks would each spawn an HttpClient and race results
        // back into the books list.
        if (_discovering) return;
        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            MessageBox.Show(this, "Enter a server URL first.", "CardDAV",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!System.Uri.TryCreate(ServerUrl, System.UriKind.Absolute, out var uri) ||
            (uri.Scheme != System.Uri.UriSchemeHttp && uri.Scheme != System.Uri.UriSchemeHttps))
        {
            MessageBox.Show(this, "Server URL must start with http:// or https://", "CardDAV",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _discovering = true;
        var senderButton = sender as System.Windows.Controls.Button;
        var prevContent = senderButton?.Content;
        if (senderButton is not null) { senderButton.IsEnabled = false; senderButton.Content = "Discovering…"; }
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            using var client = new CardDavClient(uri, Username, Password);
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
        finally
        {
            Mouse.OverrideCursor = null;
            if (senderButton is not null) { senderButton.IsEnabled = true; senderButton.Content = prevContent; }
            _discovering = false;
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
