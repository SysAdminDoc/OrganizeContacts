using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace OrganizeContacts.App.Views;

public partial class ThemedMessageDialog : Window
{
    private readonly MessageBoxButton _buttons;
    private MessageBoxResult _result = MessageBoxResult.None;

    private ThemedMessageDialog(
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        InitializeComponent();
        _buttons = buttons;
        Title = caption;
        CaptionText.Text = caption;
        MessageText.Text = message;
        ToneText.Text = ToneLabel(image);
        BuildButtons(buttons);
    }

    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        var dialog = new ThemedMessageDialog(message, caption, buttons, image);
        var resolvedOwner = owner ?? Application.Current?.MainWindow;
        if (resolvedOwner is not null && resolvedOwner.IsVisible)
        {
            dialog.Owner = resolvedOwner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _ = dialog.ShowDialog();
        return dialog._result;
    }

    private void BuildButtons(MessageBoxButton buttons)
    {
        ButtonPanel.Children.Clear();
        switch (buttons)
        {
            case MessageBoxButton.OKCancel:
                AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                AddButton("OK", MessageBoxResult.OK, isDefault: true, isPrimary: true);
                break;
            case MessageBoxButton.YesNo:
                AddButton("No", MessageBoxResult.No, isCancel: true);
                AddButton("Yes", MessageBoxResult.Yes, isDefault: true, isPrimary: true);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                AddButton("No", MessageBoxResult.No);
                AddButton("Yes", MessageBoxResult.Yes, isDefault: true, isPrimary: true);
                break;
            default:
                AddButton("OK", MessageBoxResult.OK, isDefault: true, isCancel: true, isPrimary: true);
                break;
        }
    }

    private void AddButton(
        string label,
        MessageBoxResult result,
        bool isDefault = false,
        bool isCancel = false,
        bool isPrimary = false)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 92,
            Margin = new Thickness(10, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        if (isPrimary)
            button.SetResourceReference(StyleProperty, "AccentButton");
        button.Click += (_, _) =>
        {
            _result = result;
            DialogResult = true;
        };
        ButtonPanel.Children.Add(button);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_result == MessageBoxResult.None)
            _result = FallbackResult(_buttons);
        base.OnClosing(e);
    }

    private static MessageBoxResult FallbackResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OK => MessageBoxResult.OK,
        MessageBoxButton.YesNo => MessageBoxResult.No,
        _ => MessageBoxResult.Cancel,
    };

    private static string ToneLabel(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Error => "Error",
        MessageBoxImage.Warning => "Warning",
        MessageBoxImage.Question => "Confirm",
        MessageBoxImage.Information => "Info",
        _ => "Notice",
    };
}
