using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OrganizeContacts.Core.Dedup;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Storage;

namespace OrganizeContacts.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly VCardImporter _vcard = new();
    private readonly DedupEngine _dedup = new();
    private readonly HistoryStore _history;

    public ObservableCollection<Contact> Contacts { get; } = new();
    public ObservableCollection<DuplicateGroup> Duplicates { get; } = new();

    [ObservableProperty]
    private string _statusMessage = "Ready. Use Import to load a vCard (.vcf) file.";

    [ObservableProperty]
    private Contact? _selectedContact;

    [ObservableProperty]
    private DuplicateGroup? _selectedDuplicateGroup;

    public MainViewModel()
    {
        var localData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrganizeContacts");
        _history = new HistoryStore(Path.Combine(localData, "history.sqlite"));
        _history.Audit("session.start");
    }

    [RelayCommand]
    private async Task ImportVCardAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import vCard file",
            Filter = "vCard files (*.vcf;*.vcard)|*.vcf;*.vcard|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;

        StatusMessage = $"Importing {Path.GetFileName(dlg.FileName)}…";
        var added = 0;

        await foreach (var contact in _vcard.ReadAsync(dlg.FileName))
        {
            Contacts.Add(contact);
            added++;
        }

        _history.Audit("import.vcard", payload: $"file={dlg.FileName};count={added}");
        RescanDuplicates();
        StatusMessage = $"Imported {added} contact(s) from {Path.GetFileName(dlg.FileName)}. " +
                        $"Total: {Contacts.Count}. Duplicate groups: {Duplicates.Count}.";
    }

    [RelayCommand]
    private void ClearAll()
    {
        Contacts.Clear();
        Duplicates.Clear();
        StatusMessage = "Cleared. Import again to start over.";
        _history.Audit("contacts.clear");
    }

    [RelayCommand]
    private void RescanDuplicates()
    {
        Duplicates.Clear();
        foreach (var g in _dedup.Find(Contacts)) Duplicates.Add(g);
    }
}
