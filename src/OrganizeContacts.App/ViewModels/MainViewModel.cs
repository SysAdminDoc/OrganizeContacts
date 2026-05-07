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
    private readonly ContactRepository _repo;
    private readonly HistoryStore _history;

    public ObservableCollection<Contact> Contacts { get; } = new();
    public ObservableCollection<DuplicateGroup> Duplicates { get; } = new();
    public ObservableCollection<ContactSource> Sources { get; } = new();

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
        _repo = new ContactRepository(Path.Combine(localData, "contacts.sqlite"));
        _history = new HistoryStore(_repo);
        _history.Audit("session.start");

        // Hydrate from persistent store
        foreach (var s in _repo.ListSources()) Sources.Add(s);
        foreach (var c in _repo.ListContacts()) Contacts.Add(c);
        if (Contacts.Count > 0) RescanDuplicates();
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

        var fileName = dlg.FileName;
        StatusMessage = $"Importing {Path.GetFileName(fileName)}…";

        var source = _repo.UpsertSource(new ContactSource
        {
            Kind = SourceKind.File,
            Label = Path.GetFileNameWithoutExtension(fileName),
            FilePath = fileName,
        });
        Sources.Add(source);

        var import = _repo.StartImport(new ImportRecord
        {
            SourceId = source.Id,
            FilePath = fileName,
            Status = ImportStatus.Pending,
        });

        var added = 0;
        var updated = 0;
        var skipped = 0;

        using var tx = _repo.BeginTransaction();
        await foreach (var contact in _vcard.ReadAsync(fileName))
        {
            contact.SourceId = source.Id;
            contact.ImportId = import.Id;

            // Stamp provenance on every child element
            for (int i = 0; i < contact.Phones.Count; i++)
            {
                var p = contact.Phones[i];
                if (p.SourceId is null)
                    contact.Phones[i] = new PhoneNumber
                    {
                        Raw = p.Raw,
                        Digits = p.Digits,
                        E164 = p.E164,
                        Kind = p.Kind,
                        IsPreferred = p.IsPreferred,
                        SourceId = source.Id,
                    };
            }
            for (int i = 0; i < contact.Emails.Count; i++)
            {
                var e = contact.Emails[i];
                if (e.SourceId is null)
                    contact.Emails[i] = new EmailAddress
                    {
                        Address = e.Address,
                        CanonicalOverride = e.CanonicalOverride,
                        Kind = e.Kind,
                        IsPreferred = e.IsPreferred,
                        SourceId = source.Id,
                    };
            }

            Contact? existing = null;
            if (!string.IsNullOrWhiteSpace(contact.Uid))
                existing = _repo.FindByUid(contact.Uid!, source.Id);

            if (existing is null)
            {
                _repo.InsertContact(contact, tx);
                Contacts.Add(contact);
                added++;
            }
            else if (RevIsNewer(contact.Rev, existing.Rev))
            {
                contact.Id = existing.Id;
                _repo.UpdateContact(contact, tx);
                var idx = IndexOfContact(existing.Id);
                if (idx >= 0) Contacts[idx] = contact;
                updated++;
            }
            else
            {
                skipped++;
            }
        }
        tx.Commit();

        import.FinishedAt = DateTimeOffset.UtcNow;
        import.Status = ImportStatus.Committed;
        import.ContactsCreated = added;
        import.ContactsUpdated = updated;
        import.ContactsSkipped = skipped;
        _repo.FinishImport(import);
        _history.Audit("import.vcard", payload: $"file={fileName};added={added};updated={updated};skipped={skipped}");

        RescanDuplicates();
        StatusMessage =
            $"Imported {Path.GetFileName(fileName)}: " +
            $"+{added} new, ~{updated} updated, {skipped} skipped (UID match). " +
            $"Total: {Contacts.Count}. Duplicate groups: {Duplicates.Count}.";
    }

    [RelayCommand]
    private void ClearAll()
    {
        // Soft-delete all visible contacts via journaled command.
        if (Contacts.Count == 0) return;
        using var tx = _repo.BeginTransaction();
        foreach (var c in Contacts) _repo.SoftDeleteContact(c.Id, tx);
        tx.Commit();
        Contacts.Clear();
        Duplicates.Clear();
        StatusMessage = "Cleared (soft-delete). Use Restore History to roll back.";
        _history.Audit("contacts.clear");
    }

    [RelayCommand]
    private void RescanDuplicates()
    {
        Duplicates.Clear();
        foreach (var g in _dedup.Find(Contacts)) Duplicates.Add(g);
    }

    private int IndexOfContact(Guid id)
    {
        for (int i = 0; i < Contacts.Count; i++)
            if (Contacts[i].Id == id) return i;
        return -1;
    }

    private static bool RevIsNewer(string? incoming, string? existing)
    {
        if (string.IsNullOrWhiteSpace(incoming)) return false;
        if (string.IsNullOrWhiteSpace(existing)) return true;
        return string.CompareOrdinal(incoming, existing) > 0;
    }
}
