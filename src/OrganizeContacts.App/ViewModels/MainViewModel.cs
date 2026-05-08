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
    private readonly LdifImporter _ldif = new();
    private readonly JCardImporter _jcard = new();
    private readonly VCardWriter _vcardWriter = new();
    private readonly GoogleCsvWriter _googleCsvWriter = new();
    private readonly OutlookCsvWriter _outlookCsvWriter = new();
    private readonly JCardWriter _jcardWriter = new();
    private readonly ContactRepository _repo;
    private readonly HistoryStore _history;
    private readonly RollbackService _rollback;
    private readonly MergeEngine _mergeEngine = new();
    private readonly AutoMergeService _autoMerge = new();
    private readonly CredentialVault _vault;
    // Settings-derived collaborators — reconstructed when the user saves new settings
    // so changes take effect immediately instead of requiring a restart.
    private EmailCanonicalizer _emailCanon = new();
    private readonly AppSettings _settings;
    private PhoneNormalizer _phoneNormalizer;
    private DedupEngine _dedup;

    public ObservableCollection<Contact> Contacts { get; } = new();
    public ObservableCollection<DuplicateGroup> Duplicates { get; } = new();
    public ObservableCollection<ContactSource> Sources { get; } = new();

    /// <summary>Cached `contact-id → highest confidence in any group containing it`. Refreshed
    /// after every dedup pass so <see cref="ContactPredicate"/> can decide queue membership in
    /// O(1) instead of scanning every duplicate group on every filter row.</summary>
    private Dictionary<Guid, double> _duplicateMembership = new();

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

    /// <summary>
    /// True while a background DB operation (reload, import, rescan above the threshold)
    /// is in flight. We use this to gate every command that touches the SQLite connection,
    /// because <see cref="Microsoft.Data.Sqlite.SqliteConnection"/> is not thread-safe — a
    /// UI-thread `Audit` call landing while the worker is mid-`ListContacts` would throw
    /// `SQLITE_MISUSE`.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportVCardCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportGoogleCsvCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportOutlookCsvCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportLdifCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportJCardCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCardDavCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportVCardCommand))]
    [NotifyCanExecuteChangedFor(nameof(RescanDuplicatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCleanupCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoMergeCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoLastCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReviewMergeCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenRestoreHistoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    private bool _isBusy;

    private bool NotBusy() => !IsBusy;

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

        // CollectionView must be created BEFORE the initial load so the filter is in effect
        // for the first batch — otherwise the queue selector silently does nothing on a fresh
        // session until the user types into the search box.
        ContactsView = CollectionViewSource.GetDefaultView(Contacts);
        ContactsView.Filter = ContactPredicate;

        // Initial load is synchronous because the constructor must finish before the
        // window is shown — but with the bulk-loader N+1 fix this now runs in a single
        // round trip per child table even for several thousand contacts.
        foreach (var s in _repo.ListSources()) Sources.Add(s);
        foreach (var c in _repo.ListContacts()) Contacts.Add(c);
        if (Contacts.Count > 0)
        {
            // First-load dedup runs sync so the user sees results immediately, but
            // subsequent rescans (after import/cleanup/merge) hop to a worker.
            RescanDuplicates();
        }
        if (_settings.LoadError is { Length: > 0 })
            StatusMessage = $"Settings file was unreadable; reverted to defaults (.invalid.bak preserved). {_settings.LoadError}";
        if (CredentialVault.IsSupported && _vault.CorruptVaultDetected)
            StatusMessage = "Credential vault was unreadable; backed up and started fresh.";
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
                return _duplicateMembership.ContainsKey(c.Id);
            case "Stub (only a name)":
                return c.Phones.Count == 0 && c.Emails.Count == 0 && !string.IsNullOrWhiteSpace(c.DisplayName);
            case "Empty (no name)":
                return string.IsNullOrWhiteSpace(c.DisplayName);
            case "High confidence duplicates":
                return _duplicateMembership.TryGetValue(c.Id, out var conf) && conf >= 0.85;
            default:
                return true;
        }
    }

    /// <summary>Refresh the per-contact membership map. O(n) over all duplicate-group
    /// members instead of O(n²) per ContactsView refresh row.</summary>
    private void RebuildDuplicateMembership()
    {
        var map = new Dictionary<Guid, double>(_duplicateMembership.Count);
        foreach (var g in Duplicates)
        {
            foreach (var m in g.Members)
            {
                // If a contact is in two groups (rare but possible during pair-scoring),
                // remember the higher confidence so the "high confidence" queue is honest.
                if (!map.TryGetValue(m.Id, out var existing) || g.Confidence > existing)
                    map[m.Id] = g.Confidence;
            }
        }
        _duplicateMembership = map;
    }

    private MatchRules GetMatchRules() => _settings.MatchProfile switch
    {
        "Strict" => MatchRules.Strict,
        "Loose" => MatchRules.Loose,
        _ => MatchRules.Default,
    };

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ImportVCardAsync() => await RunImport("vCard files (*.vcf;*.vcard)|*.vcf;*.vcard|All files (*.*)|*.*", _vcard, SourceKind.File);

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ImportGoogleCsvAsync() => await RunImport("Google CSV (*.csv)|*.csv|All files (*.*)|*.*", _googleCsv, SourceKind.GoogleCsv);

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ImportOutlookCsvAsync() => await RunImport("Outlook CSV (*.csv)|*.csv|All files (*.*)|*.*", _outlookCsv, SourceKind.OutlookCsv);

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ImportLdifAsync() => await RunImport("LDIF (*.ldif)|*.ldif|All files (*.*)|*.*", _ldif, SourceKind.Thunderbird);

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ImportJCardAsync() => await RunImport("jCard (*.jcard;*.jcf;*.json)|*.jcard;*.jcf;*.json|All files (*.*)|*.*", _jcard, SourceKind.File);

    [RelayCommand(CanExecute = nameof(NotBusy))]
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

        if (!Uri.TryCreate(dlg.ServerUrl, UriKind.Absolute, out var serverUri))
        {
            StatusMessage = "CardDAV import cancelled (invalid server URL).";
            return;
        }
        var importer = new CardDavImporter(
            () => new CardDavClient(serverUri, dlg.Username, dlg.Password),
            dlg.SelectedBook.Url);

        StatusMessage = $"Generating preview for CardDAV {dlg.SelectedBook.DisplayName}…";

        ContactSource source;
        ImportPreviewReport report;
        try
        {
            source = _repo.UpsertSource(new ContactSource
            {
                Kind = SourceKind.CardDav,
                Label = dlg.SelectedBook.DisplayName,
                FilePath = dlg.SelectedBook.Url,
                Account = dlg.Username,
            });
            if (!Sources.Any(x => x.Id == source.Id)) Sources.Add(source);

            var previewer = new ImportPreviewer(_repo, _phoneNormalizer, _emailCanon);
            report = await previewer.PreviewAsync(importer, dlg.SelectedBook.Url, source.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"CardDAV preview failed: {ex.Message}";
            MessageBox.Show(Application.Current.MainWindow,
                $"Could not fetch the address book:\n\n{ex.Message}",
                "CardDAV", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

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
        var pendingNew = new List<Contact>();
        var pendingUpdates = new List<Contact>();
        try
        {
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
                        _repo.InsertContact(c, tx); pendingNew.Add(c); added++;
                        break;
                    case ImportAction.UpdateNewer:
                        if (item.Existing is not null) c.Id = item.Existing.Id;
                        _repo.UpdateContact(c, tx);
                        pendingUpdates.Add(c);
                        updated++;
                        break;
                    default:
                        skipped++; break;
                }
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            import.FinishedAt = DateTimeOffset.UtcNow;
            import.Status = ImportStatus.Failed;
            import.Notes = ex.Message;
            try { _repo.FinishImport(import); } catch { }
            StatusMessage = $"CardDAV import failed mid-commit: {ex.Message}";
            MessageBox.Show(Application.Current.MainWindow,
                $"The CardDAV import was rolled back.\n\n{ex.Message}",
                "CardDAV", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        foreach (var c in pendingNew) Contacts.Add(c);
        foreach (var c in pendingUpdates)
        {
            var idx = IndexOfContact(c.Id);
            if (idx >= 0) Contacts[idx] = c;
        }

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

        ImportPreviewReport report;
        ContactSource source;
        try
        {
            source = _repo.UpsertSource(new ContactSource
            {
                Kind = kind,
                Label = Path.GetFileNameWithoutExtension(fileName),
                FilePath = fileName,
            });
            if (!Sources.Any(x => x.Id == source.Id)) Sources.Add(source);

            var previewer = new ImportPreviewer(_repo, _phoneNormalizer, _emailCanon);
            report = await previewer.PreviewAsync(importer, fileName, source.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not read {Path.GetFileName(fileName)}: {ex.Message}";
            MessageBox.Show(Application.Current.MainWindow,
                $"Could not read the file:\n\n{ex.Message}",
                "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (report.Items.Count == 0)
        {
            StatusMessage = $"{Path.GetFileName(fileName)}: nothing to import (file parsed to 0 contacts).";
            return;
        }

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
        var pendingNew = new List<Contact>();
        var pendingUpdates = new List<Contact>();

        // Stage the changes inside a transaction — only mutate the UI ObservableCollection
        // after the commit succeeds so a SQL failure mid-loop doesn't leave the in-memory
        // list ahead of the database.
        try
        {
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
                        pendingNew.Add(c);
                        added++;
                        break;
                    case ImportAction.UpdateNewer:
                        if (item.Existing is not null) c.Id = item.Existing.Id;
                        _repo.UpdateContact(c, tx);
                        pendingUpdates.Add(c);
                        updated++;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            // Mark the import as Failed so the history pane shows the truth, but leave
            // the partial rollback snapshot intact for diagnostics.
            import.FinishedAt = DateTimeOffset.UtcNow;
            import.Status = ImportStatus.Failed;
            import.Notes = ex.Message;
            try { _repo.FinishImport(import); } catch { /* don't mask the root cause */ }
            StatusMessage = $"Import failed mid-commit: {ex.Message}";
            MessageBox.Show(Application.Current.MainWindow,
                $"The import was rolled back.\n\n{ex.Message}",
                "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Commit succeeded — sync UI now.
        foreach (var c in pendingNew) Contacts.Add(c);
        foreach (var c in pendingUpdates)
        {
            var idx = IndexOfContact(c.Id);
            if (idx >= 0) Contacts[idx] = c;
        }

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

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ExportVCardAsync()
    {
        if (Contacts.Count == 0) { StatusMessage = "Nothing to export."; return; }
        var dlg = new SaveFileDialog
        {
            Title = "Export contacts",
            Filter = "vCard 3.0 (*.vcf)|*.vcf|vCard 4.0 (*.vcf)|*.vcf|Google CSV (*.csv)|*.csv|Outlook CSV (*.csv)|*.csv|jCard (*.jcard)|*.jcard",
            FileName = "OrganizeContacts.vcf",
        };
        if (dlg.ShowDialog() != true) return;

        // Snapshot once so a concurrent import / merge doesn't trip the writers' enumerators.
        var snapshot = Contacts.ToList();
        try
        {
            switch (dlg.FilterIndex)
            {
                case 1:
                    await _vcardWriter.WriteFileAsync(dlg.FileName, snapshot);
                    _history.Audit("export.vcard3", payload: $"file={dlg.FileName};count={snapshot.Count}");
                    break;
                case 2:
                    await new VCardWriter { Version = VCardVersion.V4_0 }.WriteFileAsync(dlg.FileName, snapshot);
                    _history.Audit("export.vcard4", payload: $"file={dlg.FileName};count={snapshot.Count}");
                    break;
                case 3:
                    await _googleCsvWriter.WriteFileAsync(dlg.FileName, snapshot);
                    _history.Audit("export.googlecsv", payload: $"file={dlg.FileName};count={snapshot.Count}");
                    break;
                case 4:
                    await _outlookCsvWriter.WriteFileAsync(dlg.FileName, snapshot);
                    _history.Audit("export.outlookcsv", payload: $"file={dlg.FileName};count={snapshot.Count}");
                    break;
                case 5:
                    await _jcardWriter.WriteFileAsync(dlg.FileName, snapshot);
                    _history.Audit("export.jcard", payload: $"file={dlg.FileName};count={snapshot.Count}");
                    break;
            }
            StatusMessage = $"Exported {snapshot.Count} contact(s) to {Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
            MessageBox.Show(Application.Current.MainWindow,
                $"Could not write the export:\n\n{ex.Message}",
                "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private void OpenSettings()
    {
        var dlg = new SettingsDialog(_settings) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            _settings.Save(_settingsPath);
            App.ApplyTheme(_settings.Theme);
            // Rebuild the settings-derived collaborators so a profile change takes effect
            // on the next rescan / import without needing an app restart.
            _phoneNormalizer = new PhoneNormalizer(_settings.DefaultRegion);
            _emailCanon = new EmailCanonicalizer
            {
                MergeGoogleMailDomain = _settings.MergeGoogleMailDomain,
                StripGmailDots = _settings.StripGmailDots,
                StripPlusTag = _settings.StripPlusTag,
            };
            _dedup = new DedupEngine(GetMatchRules(), _emailCanon);
            _history.Audit("settings.save");
            RescanDuplicates();
            StatusMessage = "Settings saved. Re-scanning duplicates with new rules…";
        }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
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

    [RelayCommand(CanExecute = nameof(NotBusy))]
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

    [RelayCommand(CanExecute = nameof(NotBusy))]
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

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private void ClearAll()
    {
        // Honor the visible filter — the button advertises "all visible contacts" and
        // wiping items the user can't see is a footgun (especially with the search box
        // narrowed to a single match).
        var targets = ContactsView is null
            ? Contacts.ToList()
            : ContactsView.Cast<Contact>().ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Nothing to clear (filter is empty).";
            return;
        }

        var hidden = Contacts.Count - targets.Count;
        if (_settings.ConfirmDestructiveActions)
        {
            var msg = hidden > 0
                ? $"Soft-delete the {targets.Count} visible contact(s)?  ({hidden} hidden by filter will be kept.)  You can restore them via Restore History."
                : $"Soft-delete all {targets.Count} contact(s)?  You can restore them via Restore History.";
            var ok = MessageBox.Show(Application.Current.MainWindow,
                msg, "Clear", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
        }

        var idSet = new HashSet<Guid>(targets.Select(t => t.Id));
        using var tx = _repo.BeginTransaction();
        foreach (var c in targets) _repo.SoftDeleteContact(c.Id, tx);
        tx.Commit();
        // Mutate the source collection from the back to keep indices stable.
        for (int i = Contacts.Count - 1; i >= 0; i--)
            if (idSet.Contains(Contacts[i].Id)) Contacts.RemoveAt(i);
        RescanDuplicates();
        StatusMessage = $"Cleared (soft-delete) {targets.Count} contact(s). Use Restore History to roll back.";
        _history.Audit("contacts.clear", payload: $"count={targets.Count};hidden_kept={hidden}");
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private void RunCleanup()
    {
        if (Contacts.Count == 0) { StatusMessage = "Nothing to clean."; return; }
        var dlg = new CleanupDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        var importId = Guid.NewGuid();
        // Snapshot every contact before mutating so cleanup is rollback-able.
        _rollback.CaptureForImport(importId, Contacts.Select(JsonClone).ToList(), $"before cleanup");

        var cleaner = new BatchCleanup(_phoneNormalizer, _emailCanon);
        BatchCleanupReport report;
        try
        {
            report = cleaner.Run(
                Contacts,
                dedupePhones: dlg.DedupePhones,
                dedupeEmails: dlg.DedupeEmails,
                dedupeUrls: dlg.DedupeUrls,
                dedupeCategories: dlg.DedupeCategories,
                normalizePhones: dlg.NormalizePhones,
                canonicalizeEmails: dlg.CanonicalizeEmails,
                stripPhotoMetadata: dlg.StripPhotoMetadata,
                regexEdits: dlg.Regex is null ? null : new[] { dlg.Regex });
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException ex)
        {
            StatusMessage = $"Cleanup aborted: regex timed out ({ex.Message}).";
            return;
        }

        // Persist only the rows that actually changed instead of UPDATE-ing every contact.
        // For a 5,000-contact database this is the difference between a 5,000-row write
        // and (typically) tens of writes.
        using var tx = _repo.BeginTransaction();
        foreach (var c in Contacts)
            if (report.TouchedIds.Contains(c.Id))
                _repo.UpdateContact(c, tx);
        tx.Commit();
        _history.Audit("cleanup.run", payload: report.Summary);
        RescanDuplicates();
        StatusMessage = report.Summary;
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
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

    /// <summary>Threshold at which the duplicate scan moves to a worker thread.
    /// Below this we keep the synchronous path so small libraries don't pay a context-switch
    /// tax (and tests stay single-threaded).</summary>
    private const int AsyncRescanThreshold = 500;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private void RescanDuplicates()
    {
        // Snapshot the input collection so the worker doesn't mutate the UI's
        // ObservableCollection while WPF is iterating it.
        var snapshot = Contacts.ToList();
        if (snapshot.Count < AsyncRescanThreshold)
        {
            Duplicates.Clear();
            foreach (var g in _dedup.Find(snapshot)) Duplicates.Add(g);
            RebuildDuplicateMembership();
            ContactsView?.Refresh();
            return;
        }

        StatusMessage = $"Scanning {snapshot.Count} contacts for duplicates…";
        IsBusy = true;
        _ = System.Threading.Tasks.Task.Run(() => _dedup.Find(snapshot))
            .ContinueWith(t =>
            {
                try
                {
                    if (t.IsFaulted)
                    {
                        StatusMessage = $"Duplicate scan failed: {t.Exception?.GetBaseException().Message}";
                        return;
                    }
                    Duplicates.Clear();
                    foreach (var g in t.Result) Duplicates.Add(g);
                    RebuildDuplicateMembership();
                    ContactsView?.Refresh();
                    StatusMessage = $"Found {Duplicates.Count} duplicate group(s).";
                }
                finally { IsBusy = false; }
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ReloadFromStore()
    {
        // Heavy: blocking SQLite read + dedup pass. Push to a worker so the UI keeps
        // painting; marshal results back via the captured sync context.
        // We set IsBusy *before* dispatching so the gate is closed for any UI command
        // dispatched between now and the continuation — closing the
        // single-connection-not-thread-safe race window.
        StatusMessage = "Reloading contacts…";
        IsBusy = true;
        _ = System.Threading.Tasks.Task.Run(() =>
            (contacts: _repo.ListContacts(), sources: _repo.ListSources()))
            .ContinueWith(t =>
            {
                try
                {
                    if (t.IsFaulted)
                    {
                        StatusMessage = $"Reload failed: {t.Exception?.GetBaseException().Message}";
                        return;
                    }
                    Contacts.Clear();
                    foreach (var c in t.Result.contacts) Contacts.Add(c);
                    Sources.Clear();
                    foreach (var s in t.Result.sources) Sources.Add(s);
                    StatusMessage = $"Loaded {Contacts.Count} contact(s).";
                }
                finally { IsBusy = false; }
                // Now that the gate is open again, kick off the rescan (which will
                // re-acquire IsBusy if the contact count crosses the async threshold).
                RescanDuplicates();
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
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
