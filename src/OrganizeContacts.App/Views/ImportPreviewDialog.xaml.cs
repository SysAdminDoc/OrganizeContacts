using System.IO;
using System.Windows;
using OrganizeContacts.Core.Importers;

namespace OrganizeContacts.App.Views;

public partial class ImportPreviewDialog : Window
{
    public bool CommitRequested { get; private set; }
    public bool CaptureSnapshot { get; private set; } = true;

    public ImportPreviewDialog(string filePath, ImportPreviewReport report)
    {
        InitializeComponent();
        FileNameRun.Text = Path.GetFileName(filePath);
        SummaryText.Text = report.Summary;
        Grid.ItemsSource = report.Items.Select(i => new
        {
            Action = i.Action.ToString(),
            Name = i.Incoming.DisplayName,
            Uid = i.Incoming.Uid ?? "",
            i.Reason,
        }).ToList();
    }

    private void OnCommit(object sender, RoutedEventArgs e)
    {
        CommitRequested = true;
        CaptureSnapshot = SnapshotCheckbox.IsChecked == true;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        CommitRequested = false;
        DialogResult = false;
    }
}
