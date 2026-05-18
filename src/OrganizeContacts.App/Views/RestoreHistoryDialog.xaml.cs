using System.ComponentModel;
using System.IO;
using System.Windows;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Storage;

namespace OrganizeContacts.App.Views;

public partial class RestoreHistoryDialog : Window
{
    private readonly ContactRepository _repo;
    private readonly RollbackService _rollback;

    public bool RestorePerformed { get; private set; }

    public RestoreHistoryDialog(ContactRepository repo, RollbackService rollback)
    {
        InitializeComponent();
        _repo = repo;
        _rollback = rollback;
        Refresh();
    }

    /// <summary>If the user closes via the title-bar X (or Alt+F4) after performing a restore,
    /// WPF would otherwise leave DialogResult null and the parent skip its reload — the database
    /// has already changed but the UI list would still show the pre-restore state. Always
    /// surface the in-flight restore decision through DialogResult.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!DialogResult.HasValue)
            DialogResult = RestorePerformed;
        base.OnClosing(e);
    }

    private void Refresh()
    {
        ImportsList.ItemsSource = _repo.ListImports().Select(i => new
        {
            Display = $"{Path.GetFileName(i.FilePath)} — {i.Status}",
            Detail = $"{i.StartedAt.LocalDateTime:g} · +{i.ContactsCreated} ~{i.ContactsUpdated} -{i.ContactsSkipped}",
        }).ToList();

        SnapshotList.ItemsSource = _rollback.List().Select(s => new
        {
            s.Id,
            Label = string.IsNullOrWhiteSpace(s.Label) ? s.Id.ToString()[..8] : s.Label,
            Detail = $"{s.CreatedAt.LocalDateTime:g} · import {s.ImportId.ToString()[..8]}",
        }).ToList();
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (SnapshotList.SelectedItem is null)
        {
            ThemedMessageDialog.Show(this, "Select a snapshot first.", "Restore",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var item = (dynamic)SnapshotList.SelectedItem;
        Guid id = item.Id;

        var ok = ThemedMessageDialog.Show(this,
            $"Restore snapshot {item.Label}?\n\nContacts created by this import will be removed and prior state restored.",
            "Restore",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (ok != MessageBoxResult.OK) return;

        if (_rollback.Restore(id))
        {
            RestorePerformed = true;
            Refresh();
        }
        else
        {
            ThemedMessageDialog.Show(this, "Snapshot not found.", "Restore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => DialogResult = RestorePerformed;
}
