using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OrganizeContacts.App.Views;
using OrganizeContacts.Core;
using OrganizeContacts.Core.Dedup;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.CardDav;
using OrganizeContacts.Core.Cleanup;
using OrganizeContacts.Core.Merge;
using OrganizeContacts.Core.Security;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Normalize;
using OrganizeContacts.Core.Storage;

namespace OrganizeContacts.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly string _dataDir;
    private readonly string _settingsPath;

    private readonly VCardImporter _vcard = new();
    private readonly GoogleCsvImporter _googleCsv = new();
    private readonly OutlookCsvImporter _outlookCsv = new();
    private readonly VCardWriter _vcardWriter = new();
    private readonly GoogleCsvWriter _googleCsvWriter = new();
    private readonly OutlookCsvWriter _outlookCsvWriter = new();
    private readonly ContactRepository _repo;
    private readonly HistoryStore _history;
    private readonly RollbackService _rollback;
    private readonly MergeEngine _mergeEngine = new();
    private readonly AutoMergeService _autoMerge = new();
    private readonly CredentialVault _vault;
    private readonly EmailCanonicalizer _emailCanon = new();
    private readonly AppSettings _settings;
    private readonly PhoneNormalizer _phoneNormalizer;
    private readonly DedupEngine _dedup;

    public ObservableCollection<Contact> Contacts { get; } = new();
    public ObservableCollection<DuplicateGroup> Duplicates { get; } = new();
    public ObservableCollection<ContactSource> Sources { get; } = new();

    public ICollectionView ContactsView { get; }

    public string[] ReviewQueues { get; } = new[]
    {
        "All",
        "In a duplicate group",
        "Stub (only a name)",
        "Empty (no name)",
        "High confidence duplicates",
    };

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedQueue = "All";

    partial void OnSearchTextChanged(string value) => ContactsView.Refresh();
    partial void OnSelectedQueueChanged(string value) => ContactsView.Refresh();

    [ObservableProperty]
    private string _statusMessage = "Ready. Use Import to load a vCard (.vcf) file.";

    [ObservableProperty]
    private Contact? _selectedContact;

    [ObservableProperty]
    private DuplicateGroup? _selectedDuplicateGroup;

    public MainViewModel()
    {
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrganizeContacts");
        _settingsPath = Path.Combine(_dataDir, "settings.json");
        _settings = AppSettings.LoadOrDefault(_settingsPath);

        _repo = new ContactRepository(Path.Combine(_dataDir, "contacts.sqlite"));
        _history = new HistoryStore(_repo);
        _rollback = new RollbackService(_repo);
        _vault = new CredentialVault(Path.Combine(_dataDir, "vault.dat"));
        _phoneNormalizer = new PhoneNormalizer(_settings.DefaultRegion);
        _emailCanon = new EmailCanonicalizer
        {
            MergeGoogleMailDomain = _settings.MergeGoogleMailDomain,
            StripGmailDots = _settings.StripGmailDots,
            StripPlusTag = _settings.StripPlusTag,
        };
        _dedup = new DedupEngine(GetMatchRules(), _emailCanon);

        _history.Audit("session.start");

        foreach (var s in _repo.ListSources()) Sources.Add(s);
        foreach (var c in _repo.ListContacts()) Contacts.Add(c);
        if (Contacts.Count > 0) RescanDuplicates();

        ContactsView = CollectionViewSource.GetDefaultView(Contacts);
        ContactsView.Filter = ContactPredicate;
    }

    private bool ContactPredicate(object? raw)
    {
        if (raw is not Contact c) return false;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim();
            var hay =
                (c.DisplayName ?? "") + " " +
                (c.Organization ?? "") + " " +
                string.Join(" ", c.Emails.Select(e => e.Address)) + " " +
                string.Join(" ", c.Phones.Select(p => p.Raw)) + " " +
                (c.Notes ?? "");
            if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) return false;
        }

        switch (SelectedQueue)
        {
            case "In a duplicate group":
                return Duplicates.Any(g => g.Members.Any(m => m.Id == c.Id));
            case "Stub (only a name)":
                return c.Phones.Count == 0 && c.Emails.Count == 0 && !string.IsNullOrWhiteSpace(c.DisplayName);
            case "Empty (no name)":
                return string.IsNullOrWhiteSpace(c.DisplayName);
            case "High confidence duplicates":
                var grp = Duplicates.FirstOrDefault(g => g.Members.Any(m => m.Id == c.Id));
                return grp is not null && grp.Confidence >= 0.85;
            default:
                return true;
        }
    }

    private MatchRules GetMatchRules() => _settings.MatchProfile switch
    {
        "Strict" => MatchRules.Strict,
        "Loose" => MatchRules.Loose,
        _ => MatchRules.Default,
    };

    [RelayCommand]
    private async Task ImportVCardAsync() => await RunImport("vCard files (*.vcf;*.vcard)|*.vcf;*.vcard|All files (*.*)|*.*", _vcard, SourceKind.File);

    [RelayCommand]
    private async Task ImportGoogleCsvAsync() => await RunImport("Google CSV (*.csv)|*.csv|All files (*.*)|*.*", _googleCsv, SourceKind.GoogleCsv);

    [RelayCommand]
    private async Task ImportOutlookCsvAsync() => await RunImport("Outlook CSV (*.csv)|*.csv|All files (*.*)|*.*", _outlookCsv, SourceKind.OutlookCsv);

    [RelayCommand]
    private async Task ImportCardDavAsync()
    {
        // Pre-fill from vault if Windows + an existing entry exists.
        string? url = null, user = null, pass = null;
        if (CredentialVault.IsSupported)
        {
            var saved = _vault.Get("carddav");
            if (saved is not null)
            {
                user = saved.Username;
                pass = saved.Secret;
                url = saved.Account.StartsWith("http") ? saved.Account : null;
            }
        }

        var dlg = new CardDavConnectDialog(url, user, pass) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;
        if (dlg.SelectedBook is null) return;

        if (CredentialVault.IsSupported && dlg.SaveCredentials)
            _vault.Save("carddav", dlg.Username, dlg.Password);

        var serverUri = new Uri(dlg.ServerUrl);
        var importer = new CardDavImporter(
            () => new CardDavClient(serverUri, dlg.Username, dlg.Password),
            dlg.SelectedBook.Url);

        StatusMessage = $"Generating preview for CardDAV {dlg.SelectedBook.DisplayName}…";
        var source = _repo.UpsertSource(new ContactSource
        {
            Kind = SourceKind.CardDav,
            Label = dlg.SelectedBook.DisplayName,
            FilePath = dlg.SelectedBook.Url,
            Account = dlg.Username,
        });
        if (!Sources.Any(x => x.Id == source.Id)) Sources.Add(source);

        var previewer = new ImportPreviewer(_repo, _phoneNormalizer, _emailCanon);
        var report = await previewer.PreviewAsync(importer, dlg.SelectedBook.Url, source.Id);

        var preview = new ImportPreviewDialog(dlg.SelectedBook.DisplayName, report) { Owner = Application.Current.MainWindow };
        if (preview.ShowDialog() != true)
        {
            StatusMessage = $"CardDAV import cancelled. {report.Summary}";
            return;
        }

        var import = _repo.StartImport(new ImportRecord
        {
            SourceId = source.Id,
            FilePath = dlg.SelectedBook.Url,
            Status = ImportStatus.Pending,
        });
        if (preview.CaptureSnapshot)
        {
            var touched = report.Items.Where(i => i.Existing is not null).Select(i => i.Existing!).ToList();
            if (touched.Count > 0) _rollback.CaptureForImport(import.Id, touched, $"before CardDAV {dlg.SelectedBook.DisplayName}");
        }

        var added = 0; var updated = 0; var skipped = 0;
        using var tx = _repo.BeginTransaction();
        foreach (var item in report.Items)
        {
            var c = item.Incoming;
            c.SourceId = source.Id;
            c.ImportId = import.Id;
            StampSourceOnChildren(c, source.Id);
            switch (item.Action)
            {
                case ImportAction.New:
                    _repo.InsertContact(c, tx); Contacts.Add(c); added++;
                    break;
                case ImportAction.UpdateNewer:
                    if (item.Existing is not null) c.Id = item.Existing.Id;
                    _repo.UpdateContact(c, tx);
                    var idx = IndexOfContact(c.Id);
                    if (idx >= 0) Contacts[idx] = c;
                    updated++;
                    break;
                default:
                    skipped++; break;
            }
        }
        tx.Commit();
        import.FinishedAt = DateTimeOffset.UtcNow;
        import.Status = ImportStatus.Committed;
        import.ContactsCreated = added;
        import.ContactsUpdated = updated;
        import.ContactsSkipped = skipped;
        _repo.FinishImport(import);
        _history.Audit("import.carddav", payload: $"book={dlg.SelectedBook.Url};added={added};updated={updated};skipped={skipped}");

        RescanDuplicates();
        StatusMessage =
            $"CardDAV {dlg.SelectedBook.DisplayName}: +{added} new, ~{updated} updated, {skipped} skipped. " +
            $"Total: {Contacts.Count}.";
    }

    private async Task RunImport(string filter, IContactImporter importer, SourceKind kind)
    {
        var dlg = new OpenFileDialog
        {
            Title = $"Import {importer.Name}",
            Filter = filter,
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;

        var fileName = dlg.FileName;
        StatusMessage = $"Generating preview for {Path.GetFileName(fileName)}…";

        var source = _repo.UpsertSource(new ContactSource
        {
            Kind = kind,
            Label = Path.GetFileNameWithoutExtension(fileName),
            FilePath = fileName,
        });
        if (!Sources.Any(x => x.Id == source.Id)) Sources.Add(source);

        var previewer = new ImportPreviewer(_repo, _phoneNormalizer, _emailCanon);
        var report = await previewer.PreviewAsync(importer, fileName, source.Id);

        var dialog = new ImportPreviewDialog(fileName, report) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            StatusMessage = $"Import cancelled. {report.Summary}";
            return;
        }

        var import = _repo.StartImport(new ImportRecord
        {
            SourceId = source.Id,
            FilePath = fileName,
            Status = ImportStatus.Pending,
        });

        // Capture a rollback snapshot of every contact about to be touched.
        if (dialog.CaptureSnapshot)
        {
            var touched = report.Items
                .Where(i => i.Existing is not null)
                .Select(i => i.Existing!)
                .ToList();
            if (touched.Count > 0)
                _rollback.CaptureForImport(import.Id, touched, $"before {Path.GetFileName(fileName)}");
        }

        var added = 0;
        var updated = 0;
        var skipped = 0;

        using var tx = _repo.BeginTransaction();
        foreach (var item in report.Items)
        {
            var c = item.Incoming;
            c.SourceId = source.Id;
            c.ImportId = import.Id;
            StampSourceOnChildren(c, source.Id);

            switch (item.Action)
            {
                case ImportAction.New:
                    _repo.InsertContact(c, tx);
                    Contacts.Add(c);
                    added++;
                    break;
                case ImportAction.UpdateNewer:
                    if (item.Existing is not null) c.Id = item.Existing.Id;
                    _repo.UpdateContact(c, tx);
                    var idx = IndexOfContact(c.Id);
                    if (idx >= 0) Contacts[idx] = c;
                    updated++;
                    break;
                default:
                    skipped++;
                    break;
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
            $"{Path.GetFileName(fileName)}: +{added} new, ~{updated} updated, {skipped} skipped. " +
            $"Total: {Contacts.Count}. Duplicate groups: {Duplicates.Count}.";
    }

    [RelayCommand]
    private async Task ExportVCardAsync()
    {
        if (Contacts.Count == 0) { StatusMessage = "Nothing to export."; return; }
        var dlg = new SaveFileDialog
        {
            Title = "Export contacts",
            Filter = "vCard 3.0 (*.vcf)|*.vcf|vCard 4.0 (*.vcf)|*.vcf|Google CSV (*.csv)|*.csv|Outlook CSV (*.csv)|*.csv",
            FileName = "OrganizeContacts.vcf",
        };
        if (dlg.ShowDialog() != true) return;

        switch (dlg.FilterIndex)
        {
            case 1:
                await _vcardWriter.WriteFileAsync(dlg.FileName, Contacts);
                _history.Audit("export.vcard3", payload: $"file={dlg.FileName};count={Contacts.Count}");
                break;
            case 2:
                await new VCardWriter { Version = VCardVersion.V4_0 }.WriteFileAsync(dlg.FileName, Contacts);
                _history.Audit("export.vcard4", payload: $"file={dlg.FileName};count={Contacts.Count}");
                break;
            case 3:
                await _googleCsvWriter.WriteFileAsync(dlg.FileName, Contacts.ToList());
                _history.Audit("export.googlecsv", payload: $"file={dlg.FileName};count={Contacts.Count}");
                break;
            case 4:
                await _outlookCsvWriter.WriteFileAsync(dlg.FileName, Contacts.ToList());
                _history.Audit("export.outlookcsv", payload: $"file={dlg.FileName};count={Contacts.Count}");
                break;
        }
        StatusMessage = $"Exported {Contacts.Count} contact(s) to {Path.GetFileName(dlg.FileName)}.";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var dlg = new SettingsDialog(_settings) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            _settings.Save(_settingsPath);
            _history.Audit("settings.save");
            StatusMessage = "Settings saved.  Re-import or rescan to apply.";
        }
    }

    [RelayCommand]
    private void OpenRestoreHistory()
    {
        var dlg = new RestoreHistoryDialog(_repo, _rollback) { Owner = Application.Current.MainWindow };
        var changed = dlg.ShowDialog() == true;
        if (changed)
        {
            ReloadFromStore();
            StatusMessage = "Restore complete.";
        }
    }

    [RelayCommand]
    private void ReviewMerge()
    {
        if (SelectedDuplicateGroup is null) return;
        if (SelectedDuplicateGroup.Members.Count < 2) return;

        var dlg = new MergeReviewDialog(SelectedDuplicateGroup) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        var result = _mergeEngine.Apply(dlg.Result);

        using var tx = _repo.BeginTransaction();
        _repo.UpdateContact(result.Survivor, tx);
        if (dlg.Result.DeleteSecondaries)
            foreach (var sec in result.RemovedSecondaries)
                _repo.SoftDeleteContact(sec.Id, tx);
        tx.Commit();

        _history.RecordUndo(
            "merge",
            new { primary = result.Survivor.Id, removed = result.RemovedSecondaries.Select(c => c.Id) },
            new { restored = result.RemovedSecondaries.Select(c => c.Id) },
            $"merge {result.Survivor.DisplayName}");

        ReloadFromStore();
        StatusMessage = $"Merged into '{result.Survivor.DisplayName}'.";
    }

    [RelayCommand]
    private void UndoLast()
    {
        var entry = _history.GetMostRecentApplied();
        if (entry is null)
        {
            StatusMessage = "Nothing to undo.";
            return;
        }
        if (_settings.ConfirmDestructiveActions)
        {
            var ok = MessageBox.Show(Application.Current.MainWindow,
                $"Undo {entry.Op}: {entry.Label}?",
                "Undo",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (ok != MessageBoxResult.OK) return;
        }

        // For merge: re-insert removed contacts via reading the inverse JSON.
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(entry.InverseJson);
            if (entry.Op == "merge" && doc.RootElement.TryGetProperty("restored", out var restored))
            {
                using var tx = _repo.BeginTransaction();
                foreach (var idEl in restored.EnumerateArray())
                {
                    var id = Guid.Parse(idEl.GetString()!);
                    _repo.RestoreContact(id, tx);
                }
                tx.Commit();
            }
            _history.MarkUndone(entry.Id);
            ReloadFromStore();
            StatusMessage = $"Undone: {entry.Label ?? entry.Op}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Undo failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        if (Contacts.Count == 0) return;
        if (_settings.ConfirmDestructiveActions)
        {
            var ok = MessageBox.Show(Application.Current.MainWindow,
                $"Soft-delete all {Contacts.Count} contact(s)? You can restore them via Restore History.",
                "Clear",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
        }

        using var tx = _repo.BeginTransaction();
        foreach (var c in Contacts) _repo.SoftDeleteContact(c.Id, tx);
        tx.Commit();
        Contacts.Clear();
        Duplicates.Clear();
        StatusMessage = "Cleared (soft-delete).  Use Restore History to roll back.";
        _history.Audit("contacts.clear");
    }

    [RelayCommand]
    private void RunCleanup()
    {
        if (Contacts.Count == 0) { StatusMessage = "Nothing to clean."; return; }
        var dlg = new CleanupDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        var importId = Guid.NewGuid();
        // Snapshot every contact before mutating so cleanup is rollback-able.
        _rollback.CaptureForImport(importId, Contacts.Select(JsonClone).ToList(), $"before cleanup");

        var cleaner = new BatchCleanup(_phoneNormalizer, _emailCanon);
        var report = cleaner.Run(
            Contacts,
            dedupePhones: dlg.DedupePhones,
            dedupeEmails: dlg.DedupeEmails,
            dedupeUrls: dlg.DedupeUrls,
            dedupeCategories: dlg.DedupeCategories,
            normalizePhones: dlg.NormalizePhones,
            canonicalizeEmails: dlg.CanonicalizeEmails,
            stripPhotoMetadata: dlg.StripPhotoMetadata,
            regexEdits: dlg.Regex is null ? null : new[] { dlg.Regex });

        using var tx = _repo.BeginTransaction();
        foreach (var c in Contacts) _repo.UpdateContact(c, tx);
        tx.Commit();
        _history.Audit("cleanup.run", payload: report.Summary);
        RescanDuplicates();
        StatusMessage = report.Summary;
    }

    [RelayCommand]
    private void AutoMerge()
    {
        if (Duplicates.Count == 0) { StatusMessage = "No duplicates."; return; }
        var rules = GetMatchRules();
        var plan = _autoMerge.Plan(Duplicates, rules.AutoMergeThreshold);
        if (plan.Plans.Count == 0)
        {
            StatusMessage = $"Auto-merge planned 0 / {Duplicates.Count} groups (need ≥ {rules.AutoMergeThreshold:P0} confidence + subset secondaries).";
            return;
        }
        if (_settings.ConfirmDestructiveActions)
        {
            var ok = MessageBox.Show(Application.Current.MainWindow,
                $"Auto-merge {plan.Plans.Count} group(s)?  Each merge is rollback-able via undo.",
                "Auto-merge",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (ok != MessageBoxResult.OK) return;
        }

        var merged = 0;
        foreach (var p in plan.Plans)
        {
            var result = _mergeEngine.Apply(p);
            using var tx = _repo.BeginTransaction();
            _repo.UpdateContact(result.Survivor, tx);
            foreach (var sec in result.RemovedSecondaries) _repo.SoftDeleteContact(sec.Id, tx);
            tx.Commit();
            _history.RecordUndo(
                "merge",
                new { primary = result.Survivor.Id, removed = result.RemovedSecondaries.Select(c => c.Id) },
                new { restored = result.RemovedSecondaries.Select(c => c.Id) },
                $"auto-merge {result.Survivor.DisplayName}");
            merged++;
        }
        ReloadFromStore();
        StatusMessage = $"Auto-merged {merged} group(s).  {plan.Skipped} skipped for manual review.";
    }

    private static Contact JsonClone(Contact src)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(src);
        return System.Text.Json.JsonSerializer.Deserialize<Contact>(json)!;
    }

    [RelayCommand]
    private void RescanDuplicates()
    {
        Duplicates.Clear();
        foreach (var g in _dedup.Find(Contacts)) Duplicates.Add(g);
        ContactsView?.Refresh();
    }

    private void ReloadFromStore()
    {
        Contacts.Clear();
        foreach (var c in _repo.ListContacts()) Contacts.Add(c);
        Sources.Clear();
        foreach (var s in _repo.ListSources()) Sources.Add(s);
        RescanDuplicates();
    }

    private int IndexOfContact(Guid id)
    {
        for (int i = 0; i < Contacts.Count; i++)
            if (Contacts[i].Id == id) return i;
        return -1;
    }

    private static void StampSourceOnChildren(Contact c, Guid sourceId)
    {
        for (int i = 0; i < c.Phones.Count; i++)
        {
            var p = c.Phones[i];
            if (p.SourceId is null)
                c.Phones[i] = new PhoneNumber
                {
                    Raw = p.Raw,
                    Digits = p.Digits,
                    E164 = p.E164,
                    Kind = p.Kind,
                    IsPreferred = p.IsPreferred,
                    SourceId = sourceId,
                };
        }
        for (int i = 0; i < c.Emails.Count; i++)
        {
            var e = c.Emails[i];
            if (e.SourceId is null)
                c.Emails[i] = new EmailAddress
                {
                    Address = e.Address,
                    CanonicalOverride = e.CanonicalOverride,
                    Kind = e.Kind,
                    IsPreferred = e.IsPreferred,
                    SourceId = sourceId,
                };
        }
    }
}
