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
    private static readonly Guid ToolSourceId = Guid.Parse("4efbb1a1-7530-4d6e-8ce1-5f6b19762b42");

    private readonly string _dataDir;
    private readonly string _settingsPath;

    private readonly VCardImporter _vcard = new();
    private readonly GoogleCsvImporter _googleCsv = new();
    private readonly OutlookCsvImporter _outlookCsv = new();
    private readonly LdifImporter _ldif = new();
    private readonly JCardImporter _jcard = new();
    private readonly ContactImportCatalog _importCatalog;
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
    private string _statusMessage = "Ready. Import a folder or choose an individual contact file to begin.";

    [ObservableProperty]
    private Contact? _selectedContact;

    [ObservableProperty]
    private DuplicateGroup? _selectedDuplicateGroup;

    partial void OnSelectedDuplicateGroupChanged(DuplicateGroup? value) =>
        ReviewMergeCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// True while a database operation is in flight. This gates commands that touch the
    /// single SQLite connection, but duplicate scans run from an in-memory snapshot and
    /// use their own state so tools do not stay disabled after a large import.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportVCardCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportFolderCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(ClearAllContactsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReviewMergeCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenRestoreHistoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RescanDuplicatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoMergeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReviewMergeCommand))]
    private bool _isDuplicateScanRunning;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressLabel = string.Empty;

    private string? _progressOwner;

    private bool NotBusy() => !IsBusy;
    private bool CanStartDuplicateScan() => !IsBusy && !IsDuplicateScanRunning;
    private bool CanUseDuplicateResults() => !IsBusy && !IsDuplicateScanRunning;

    private void BeginProgress(string label, bool indeterminate, double value = 0, string? owner = null)
    {
        _progressOwner = owner;
        ProgressLabel = label;
        ProgressValue = Math.Clamp(value, 0, 100);
        IsProgressIndeterminate = indeterminate;
        IsProgressVisible = true;
    }

    private void ReportProgress(string label, double value)
    {
        ProgressLabel = label;
        ProgressValue = Math.Clamp(value, 0, 100);
        IsProgressIndeterminate = false;
        IsProgressVisible = true;
    }

    private void EndProgress(string? owner = null)
    {
        if (owner is not null && _progressOwner != owner) return;
        _progressOwner = null;
        IsProgressVisible = false;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        ProgressLabel = string.Empty;
    }

    private ContactSource EnsureToolSource()
    {
        var source = _repo.UpsertSource(new ContactSource
        {
            Id = ToolSourceId,
            Kind = SourceKind.Manual,
            Label = "OrganizeContacts tools",
            FilePath = "organizecontacts://tools",
        });
        if (!Sources.Any(x => x.Id == source.Id)) Sources.Add(source);
        return source;
    }

    private ImportRecord StartToolImport(string operation)
    {
        var source = EnsureToolSource();
        return _repo.StartImport(new ImportRecord
        {
            SourceId = source.Id,
            FilePath = $"tool:{operation}",
            Status = ImportStatus.Pending,
            Notes = operation,
        });
    }

    private void FinishToolImport(ImportRecord import, int updated, int skipped, string? notes = null)
    {
        import.FinishedAt = DateTimeOffset.UtcNow;
        import.Status = ImportStatus.Committed;
        import.ContactsUpdated = updated;
        import.ContactsSkipped = skipped;
        import.Notes = notes ?? import.Notes;
        _repo.FinishImport(import);
    }

    private void MarkToolImportFailed(ImportRecord? import, string message)
    {
        if (import is null) return;
        try
        {
            import.FinishedAt = DateTimeOffset.UtcNow;
            import.Status = ImportStatus.Failed;
            import.Notes = message;
            _repo.FinishImport(import);
        }
        catch
        {
            // Preserve the original tool failure message.
        }
    }

    public MainViewModel()
    {
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrganizeContacts");
        _settingsPath = Path.Combine(_dataDir, "settings.json");
        _settings = AppSettings.LoadOrDefault(_settingsPath);
        _importCatalog = new ContactImportCatalog(new[]
        {
            new ContactImportFormat(_vcard, SourceKind.File),
            new ContactImportFormat(_googleCsv, SourceKind.GoogleCsv),
            new ContactImportFormat(_outlookCsv, SourceKind.OutlookCsv),
            new ContactImportFormat(_ldif, SourceKind.Thunderbird),
            new ContactImportFormat(_jcard, SourceKind.File),
        });

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
            // First-load dedup is skipped for very large libraries so the app does not
            // open into a disabled toolbar after a bulk import.
            RescanDuplicatesCore(userInitiated: false);
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
    private async Task ImportFolderAsync()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Import a folder of contact files",
        };
        if (dlg.ShowDialog() != true) return;

        var folder = dlg.FolderName;
        var detected = _importCatalog.FindFiles(folder);
        if (detected.Count == 0)
        {
            StatusMessage = $"{Path.GetFileName(folder)}: no supported contact files found.";
            ThemedMessageDialog.Show(Application.Current.MainWindow,
                "No supported contact files were found in that folder.\n\nSupported formats: vCard, Google CSV, Outlook CSV, LDIF, and jCard.",
                "Import folder", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StatusMessage = $"Generating preview for {detected.Count} file(s) in {Path.GetFileName(folder)}...";
        IsBusy = true;
        BeginProgress($"Previewing 0/{detected.Count} files", indeterminate: false, owner: "import-folder");
        var batches = new List<PendingImportBatch>();
        var aggregate = new ImportPreviewReport();
        var previewFailures = new List<string>();
        try
        {
            var completed = 0;
            foreach (var file in detected)
            {
                ReportProgress(
                    $"Previewing {completed + 1}/{detected.Count}: {Path.GetFileName(file.FilePath)}",
                    completed * 100.0 / detected.Count);
                await System.Threading.Tasks.Task.Yield();
                try
                {
                    var (source, report) = await PreviewImportFileAsync(
                        file.FilePath,
                        file.Format.Importer,
                        file.Format.SourceKind);
                    if (report.Items.Count > 0)
                    {
                        batches.Add(new PendingImportBatch(file.FilePath, source, report));
                        foreach (var item in report.Items) aggregate.Items.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    previewFailures.Add($"{Path.GetFileName(file.FilePath)}: {ex.Message}");
                }
                // Always tick: previously a `continue` after an empty file's preview skipped
                // the increment, leaving the progress bar stalled until the next non-empty file.
                completed++;
                ReportProgress(
                    $"Previewed {completed}/{detected.Count} files",
                    completed * 100.0 / detected.Count);
            }
        }
        finally
        {
            EndProgress("import-folder");
            IsBusy = false;
        }

        if (aggregate.Items.Count == 0)
        {
            StatusMessage = $"{Path.GetFileName(folder)}: supported files parsed to 0 contacts.";
            ShowImportFailures("Import folder", previewFailures);
            return;
        }

        if (previewFailures.Count > 0)
            ShowImportFailures("Some files could not be previewed", previewFailures);

        var dialogTitle = $"{Path.GetFileName(folder)} ({batches.Count} file(s))";
        var dialog = new ImportPreviewDialog(dialogTitle, aggregate) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            StatusMessage = $"Folder import cancelled. {aggregate.Summary}";
            return;
        }

        IsBusy = true;
        BeginProgress($"Importing 0/{batches.Count} files", indeterminate: false, owner: "import-folder");
        var totals = new ImportCommitTotals();
        var commitFailures = new List<string>();
        try
        {
            var completed = 0;
            foreach (var batch in batches)
            {
                try
                {
                    var result = await CommitImportReportAsync(
                        batch.Source,
                        batch.FilePath,
                        batch.Report,
                        dialog.CaptureSnapshot,
                        "import.folder",
                        $"Importing {completed + 1}/{batches.Count}: {Path.GetFileName(batch.FilePath)}");
                    totals.Add(result);
                    totals.FilesCommitted++;
                }
                catch (Exception ex)
                {
                    commitFailures.Add($"{Path.GetFileName(batch.FilePath)}: {ex.Message}");
                }
                completed++;
                ReportProgress(
                    $"Imported {completed}/{batches.Count} files",
                    completed * 100.0 / batches.Count);
                await System.Threading.Tasks.Task.Yield();
            }
        }
        finally
        {
            EndProgress("import-folder");
            IsBusy = false;
        }

        var autoScanRan = RescanDuplicatesCore(userInitiated: false);
        if (commitFailures.Count > 0)
            ShowImportFailures("Some files could not be committed", commitFailures);

        StatusMessage =
            $"{Path.GetFileName(folder)}: committed {totals.FilesCommitted}/{batches.Count} file(s), " +
            $"+{totals.Added} new, ~{totals.Updated} updated, {totals.Skipped} skipped. " +
            $"Total: {Contacts.Count}. " +
            (autoScanRan ? $"Duplicate groups: {Duplicates.Count}." : "Duplicate scan skipped for this large library; use Rescan when ready.");
    }

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
        IsBusy = true;
        BeginProgress($"Previewing CardDAV {dlg.SelectedBook.DisplayName}", indeterminate: true, owner: "carddav");
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
            ThemedMessageDialog.Show(Application.Current.MainWindow,
                $"Could not fetch the address book:\n\n{ex.Message}",
                "CardDAV", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            EndProgress("carddav");
            IsBusy = false;
        }

        var preview = new ImportPreviewDialog(dlg.SelectedBook.DisplayName, report) { Owner = Application.Current.MainWindow };
        if (preview.ShowDialog() != true)
        {
            StatusMessage = $"CardDAV import cancelled. {report.Summary}";
            return;
        }

        IsBusy = true;
        BeginProgress($"Importing CardDAV {dlg.SelectedBook.DisplayName}", indeterminate: false, owner: "carddav");
        ImportCommitResult result;
        try
        {
            result = await CommitImportReportAsync(
                source,
                dlg.SelectedBook.Url,
                report,
                preview.CaptureSnapshot,
                "import.carddav",
                $"Importing CardDAV {dlg.SelectedBook.DisplayName}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"CardDAV import failed mid-commit: {ex.Message}";
            ThemedMessageDialog.Show(Application.Current.MainWindow,
                $"The CardDAV import was rolled back.\n\n{ex.Message}",
                "CardDAV", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            EndProgress("carddav");
            IsBusy = false;
        }

        var autoScanRan = RescanDuplicatesCore(userInitiated: false);
        StatusMessage =
            $"CardDAV {dlg.SelectedBook.DisplayName}: +{result.Added} new, ~{result.Updated} updated, {result.Skipped} skipped. " +
            $"Total: {Contacts.Count}." +
            (autoScanRan ? "" : " Duplicate scan skipped for this large library; use Rescan when ready.");
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
        await RunImportFileAsync(fileName, importer, kind);
    }

    private async Task RunImportFileAsync(string fileName, IContactImporter importer, SourceKind kind)
    {
        StatusMessage = $"Generating preview for {Path.GetFileName(fileName)}…";

        ImportPreviewReport report;
        ContactSource source;
        IsBusy = true;
        BeginProgress($"Previewing {Path.GetFileName(fileName)}", indeterminate: true, owner: "import-file");
        try
        {
            (source, report) = await PreviewImportFileAsync(fileName, importer, kind);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not read {Path.GetFileName(fileName)}: {ex.Message}";
            ThemedMessageDialog.Show(Application.Current.MainWindow,
                $"Could not read the file:\n\n{ex.Message}",
                "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            EndProgress("import-file");
            IsBusy = false;
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

        IsBusy = true;
        BeginProgress($"Importing {Path.GetFileName(fileName)}", indeterminate: false, owner: "import-file");
        ImportCommitResult result;
        try
        {
            result = await CommitImportReportAsync(
                source,
                fileName,
                report,
                dialog.CaptureSnapshot,
                "import.file",
                $"Importing {Path.GetFileName(fileName)}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed mid-commit: {ex.Message}";
            ThemedMessageDialog.Show(Application.Current.MainWindow,
                $"The import was rolled back.\n\n{ex.Message}",
                "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            EndProgress("import-file");
            IsBusy = false;
        }

        var autoScanRan = RescanDuplicatesCore(userInitiated: false);
        StatusMessage =
            $"{Path.GetFileName(fileName)}: +{result.Added} new, ~{result.Updated} updated, {result.Skipped} skipped. " +
            $"Total: {Contacts.Count}. " +
            (autoScanRan ? $"Duplicate groups: {Duplicates.Count}." : "Duplicate scan skipped for this large library; use Rescan when ready.");
    }

    private async Task<(ContactSource Source, ImportPreviewReport Report)> PreviewImportFileAsync(
        string fileName,
        IContactImporter importer,
        SourceKind kind)
    {
        var source = _repo.UpsertSource(new ContactSource
        {
            Kind = kind,
            Label = Path.GetFileNameWithoutExtension(fileName),
            FilePath = fileName,
        });
        if (!Sources.Any(x => x.Id == source.Id)) Sources.Add(source);

        var previewer = new ImportPreviewer(_repo, _phoneNormalizer, _emailCanon);
        var report = await previewer.PreviewAsync(importer, fileName, source.Id);
        return (source, report);
    }

    private async Task<ImportCommitResult> CommitImportReportAsync(
        ContactSource source,
        string fileName,
        ImportPreviewReport report,
        bool captureSnapshot,
        string auditOperation,
        string progressLabel)
    {
        InvalidateDuplicateScan();
        var import = _repo.StartImport(new ImportRecord
        {
            SourceId = source.Id,
            FilePath = fileName,
            Status = ImportStatus.Pending,
        });

        if (captureSnapshot)
        {
            var touched = report.Items
                .Where(i => i.Existing is not null)
                .Select(i => i.Existing!)
                .ToList();
            if (touched.Count > 0)
            {
                ReportProgress($"{progressLabel}: saving rollback snapshot", 2);
                await System.Threading.Tasks.Task.Yield();
                _rollback.CaptureForImport(import.Id, touched, $"before {Path.GetFileName(fileName)}");
            }
        }

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var pendingNew = new List<Contact>();
        var pendingUpdates = new List<Contact>();
        var processed = 0;
        var total = Math.Max(report.Items.Count, 1);

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

                processed++;
                if (processed % 100 == 0 || processed == total)
                {
                    ReportProgress($"{progressLabel} ({processed}/{total} contacts)", processed * 90.0 / total);
                    await System.Threading.Tasks.Task.Yield();
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
            throw;
        }

        var uiProcessed = 0;
        var uiTotal = Math.Max(pendingNew.Count + pendingUpdates.Count, 1);
        ReportProgress($"{progressLabel}: updating list", 92);
        foreach (var c in pendingNew)
        {
            Contacts.Add(c);
            uiProcessed++;
            if (uiProcessed % 250 == 0 || uiProcessed == uiTotal)
            {
                ReportProgress($"{progressLabel}: updating list", 92 + uiProcessed * 8.0 / uiTotal);
                await System.Threading.Tasks.Task.Yield();
            }
        }

        // Pre-build an id → index map so the pendingUpdates loop is O(N) instead of
        // O(N×M). For 10K imports landing on a 10K library this turned an avoidable
        // 10²s of UI-thread time into a few ms.
        var indexById = pendingUpdates.Count > 0 ? BuildContactIndexMap() : null;
        foreach (var c in pendingUpdates)
        {
            if (indexById!.TryGetValue(c.Id, out var idx) && idx < Contacts.Count && Contacts[idx].Id == c.Id)
                Contacts[idx] = c;
            else
            {
                // Fallback in case the collection was mutated mid-loop (unlikely on the UI
                // thread but defensive against future async changes).
                var live = IndexOfContact(c.Id);
                if (live >= 0) Contacts[live] = c;
            }
            uiProcessed++;
            if (uiProcessed % 250 == 0 || uiProcessed == uiTotal)
            {
                ReportProgress($"{progressLabel}: updating list", 92 + uiProcessed * 8.0 / uiTotal);
                await System.Threading.Tasks.Task.Yield();
            }
        }

        import.FinishedAt = DateTimeOffset.UtcNow;
        import.Status = ImportStatus.Committed;
        import.ContactsCreated = added;
        import.ContactsUpdated = updated;
        import.ContactsSkipped = skipped;
        _repo.FinishImport(import);
        _history.Audit(auditOperation, payload: $"file={fileName};added={added};updated={updated};skipped={skipped}");

        return new ImportCommitResult(added, updated, skipped);
    }

    private static void ShowImportFailures(string caption, IReadOnlyList<string> failures)
    {
        if (failures.Count == 0) return;

        var shown = failures.Take(8).ToList();
        var suffix = failures.Count > shown.Count
            ? $"{Environment.NewLine}{Environment.NewLine}+{failures.Count - shown.Count} more."
            : string.Empty;
        ThemedMessageDialog.Show(Application.Current.MainWindow,
            string.Join(Environment.NewLine, shown) + suffix,
            caption,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
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
        IsBusy = true;
        BeginProgress($"Exporting {snapshot.Count} contacts", indeterminate: true, owner: "export");
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
            ThemedMessageDialog.Show(Application.Current.MainWindow,
                $"Could not write the export:\n\n{ex.Message}",
                "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndProgress("export");
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private void OpenSettings()
    {
        var dlg = new SettingsDialog(_settings) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            InvalidateDuplicateScan();
            try
            {
                _settings.Save(_settingsPath);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Settings could not be persisted: {ex.Message}";
                ThemedMessageDialog.Show(Application.Current.MainWindow,
                    $"Settings will apply for this session but could not be saved to disk:\n\n{ex.Message}",
                    "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                // Continue applying in-memory — better UX than dropping the user's edits.
            }
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
            var autoScanRan = RescanDuplicatesCore(userInitiated: false);
            StatusMessage = autoScanRan
                ? "Settings saved. Re-scanning duplicates with new rules..."
                : "Settings saved. Duplicate scan skipped for this large library; use Rescan when ready.";
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

    private bool CanReviewMerge() =>
        !IsBusy && !IsDuplicateScanRunning && SelectedDuplicateGroup is { Members.Count: >= 2 };

    [RelayCommand(CanExecute = nameof(CanReviewMerge))]
    private void ReviewMerge()
    {
        if (SelectedDuplicateGroup is null) return;
        if (SelectedDuplicateGroup.Members.Count < 2) return;

        var dlg = new MergeReviewDialog(SelectedDuplicateGroup) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        InvalidateDuplicateScan();
        // Capture the survivor's pre-merge state BEFORE applying — without it, undo would
        // restore the secondaries but leave the survivor still holding their merged
        // collections, creating duplicates of the data we just unified.
        var primaryBefore = JsonClone(dlg.Result.Primary);
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
            new { primaryBefore, restored = result.RemovedSecondaries.Select(c => c.Id) },
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
            var ok = ThemedMessageDialog.Show(Application.Current.MainWindow,
                $"Undo {entry.Op}: {entry.Label}?",
                "Undo",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (ok != MessageBoxResult.OK) return;
        }

        // For merge: revert the survivor to its pre-merge state AND un-soft-delete the
        // removed secondaries. The previous version only un-deleted secondaries, leaving
        // the survivor still holding the merged-in collections — so undoing a merge
        // recreated duplicates of the data we'd just unified.
        try
        {
            InvalidateDuplicateScan();
            using var doc = System.Text.Json.JsonDocument.Parse(entry.InverseJson);
            if (entry.Op == "merge")
            {
                using var tx = _repo.BeginTransaction();
                var root = doc.RootElement;

                // Survivor pre-merge state (newer entries) — older entries lack this field
                // and we degrade gracefully (secondary-only restore).
                if (root.TryGetProperty("primaryBefore", out var primaryBefore) &&
                    primaryBefore.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var pre = System.Text.Json.JsonSerializer.Deserialize<Contact>(primaryBefore.GetRawText());
                    if (pre is not null && _repo.ExistsAnyState(pre.Id))
                    {
                        _repo.UpdateContact(pre, tx);
                        // If a follow-up Clear soft-deleted the survivor, restore it too.
                        _repo.RestoreContact(pre.Id, tx);
                    }
                }

                if (root.TryGetProperty("restored", out var restored) &&
                    restored.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var idEl in restored.EnumerateArray())
                    {
                        if (idEl.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                        if (!Guid.TryParse(idEl.GetString(), out var id)) continue;
                        _repo.RestoreContact(id, tx);
                    }
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
    private async Task ClearAllAsync()
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
        var msg = hidden > 0
            ? $"Soft-delete the {targets.Count} visible contact(s)?  ({hidden} hidden by filter will be kept.)  You can restore them via Restore History."
            : $"Soft-delete all {targets.Count} contact(s)?  You can restore them via Restore History.";
        await ClearTargetsAsync(targets, "Clear visible", msg, $"hidden_kept={hidden}");
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ClearAllContactsAsync()
    {
        var targets = Contacts.ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Nothing to clear.";
            return;
        }

        var msg =
            $"Soft-delete all {targets.Count} imported contact(s), ignoring search and queue filters?  You can restore them via Restore History.";
        await ClearTargetsAsync(targets, "Clear all contacts", msg, "scope=all");
    }

    private async Task ClearTargetsAsync(IReadOnlyList<Contact> targets, string title, string message, string auditSuffix)
    {
        if (_settings.ConfirmDestructiveActions)
        {
            var ok = ThemedMessageDialog.Show(Application.Current.MainWindow,
                message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
        }

        IsBusy = true;
        BeginProgress($"Clearing 0/{targets.Count} contacts", indeterminate: false, owner: "clear");
        ImportRecord? operation = null;
        try
        {
            InvalidateDuplicateScan();
            operation = StartToolImport(title);
            var rollbackTargets = new List<Contact>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                rollbackTargets.Add(JsonClone(targets[i]));
                var processed = i + 1;
                if (processed % 250 == 0 || processed == targets.Count)
                {
                    ReportProgress($"Preparing restore snapshot {processed}/{targets.Count}", processed * 12.0 / targets.Count);
                    await System.Threading.Tasks.Task.Yield();
                }
            }
            ReportProgress("Saving restore snapshot", 14);
            await System.Threading.Tasks.Task.Yield();
            _rollback.CaptureForImport(operation.Id, rollbackTargets, $"before {title}");

            var idSet = new HashSet<Guid>(targets.Select(t => t.Id));
            using (var tx = _repo.BeginTransaction())
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    _repo.SoftDeleteContact(targets[i].Id, tx);
                    var processed = i + 1;
                    if (processed % 100 == 0 || processed == targets.Count)
                    {
                        ReportProgress(
                            $"Clearing {processed}/{targets.Count} contacts",
                            15 + processed * 70.0 / targets.Count);
                        await System.Threading.Tasks.Task.Yield();
                    }
                }
                tx.Commit();
            }

            ReportProgress("Updating contact list", 90);
            await System.Threading.Tasks.Task.Yield();

            if (idSet.Count == Contacts.Count)
            {
                Contacts.Clear();
            }
            else
            {
                using var defer = ContactsView.DeferRefresh();
                var checkedRows = 0;
                for (int i = Contacts.Count - 1; i >= 0; i--)
                {
                    checkedRows++;
                    if (idSet.Contains(Contacts[i].Id)) Contacts.RemoveAt(i);
                    if (checkedRows % 500 == 0)
                    {
                        ReportProgress("Updating contact list", 90 + checkedRows * 10.0 / Math.Max(Contacts.Count + checkedRows, 1));
                        await System.Threading.Tasks.Task.Yield();
                    }
                }
            }

            ReportProgress("Updating duplicate review", 100);
            IsBusy = false;
            EndProgress("clear");
            RescanDuplicatesCore(userInitiated: false);
            StatusMessage = $"Cleared (soft-delete) {targets.Count} contact(s). Use Restore History to roll back.";
            FinishToolImport(operation, updated: targets.Count, skipped: 0, notes: title);
            _history.Audit("contacts.clear", payload: $"count={targets.Count};{auditSuffix}");
        }
        catch (Exception ex)
        {
            MarkToolImportFailed(operation, ex.Message);
            StatusMessage = $"Clear failed: {ex.Message}";
            ThemedMessageDialog.Show(Application.Current.MainWindow,
                $"Could not clear contacts:\n\n{ex.Message}",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (IsBusy) IsBusy = false;
            EndProgress("clear");
        }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task RunCleanupAsync()
    {
        if (Contacts.Count == 0) { StatusMessage = "Nothing to clean."; return; }
        var dlg = new CleanupDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        BeginProgress("Preparing cleanup snapshot", indeterminate: false, owner: "cleanup");
        ImportRecord? operation = null;
        try
        {
            // Snapshot every contact before mutating so cleanup is rollback-able.
            InvalidateDuplicateScan();
            operation = StartToolImport("cleanup");
            var snapshot = new List<Contact>(Contacts.Count);
            for (int i = 0; i < Contacts.Count; i++)
            {
                snapshot.Add(JsonClone(Contacts[i]));
                var processed = i + 1;
                if (processed % 100 == 0 || processed == Contacts.Count)
                {
                    ReportProgress("Preparing cleanup snapshot", processed * 20.0 / Contacts.Count);
                    await System.Threading.Tasks.Task.Yield();
                }
            }
            ReportProgress("Saving cleanup snapshot", 20);
            await System.Threading.Tasks.Task.Yield();
            _rollback.CaptureForImport(operation.Id, snapshot, $"before cleanup");

            var cleaner = new BatchCleanup(_phoneNormalizer, _emailCanon);
            var report = new BatchCleanupReport();
            var regexEdits = dlg.Regex is null ? null : new[] { dlg.Regex };
            var cleaned = new List<Contact>(snapshot.Count);
            for (int i = 0; i < snapshot.Count; i++)
                cleaned.Add(JsonClone(snapshot[i]));

            for (int i = 0; i < cleaned.Count; i++)
            {
                var partial = cleaner.Run(
                    new[] { cleaned[i] },
                    dedupePhones: dlg.DedupePhones,
                    dedupeEmails: dlg.DedupeEmails,
                    dedupeUrls: dlg.DedupeUrls,
                    dedupeCategories: dlg.DedupeCategories,
                    normalizePhones: dlg.NormalizePhones,
                    canonicalizeEmails: dlg.CanonicalizeEmails,
                    stripPhotoMetadata: dlg.StripPhotoMetadata,
                    regexEdits: regexEdits);
                AddCleanupReport(report, partial);

                var processed = i + 1;
                if (processed % 100 == 0 || processed == cleaned.Count)
                {
                    ReportProgress($"Cleaning {processed}/{cleaned.Count} contacts", 20 + processed * 45.0 / cleaned.Count);
                    await System.Threading.Tasks.Task.Yield();
                }
            }

            // Persist only the rows that actually changed instead of UPDATE-ing every contact.
            using var tx = _repo.BeginTransaction();
            var persisted = 0;
            var touchedTotal = Math.Max(report.TouchedIds.Count, 1);
            foreach (var c in cleaned)
            {
                if (!report.TouchedIds.Contains(c.Id)) continue;
                _repo.UpdateContact(c, tx);
                persisted++;
                if (persisted % 100 == 0 || persisted == touchedTotal)
                {
                    ReportProgress($"Saving cleanup {persisted}/{touchedTotal} contacts", 65 + persisted * 35.0 / touchedTotal);
                    await System.Threading.Tasks.Task.Yield();
                }
            }
            tx.Commit();

            var indexById = report.TouchedIds.Count > 0 ? BuildContactIndexMap() : null;
            var uiUpdated = 0;
            foreach (var c in cleaned)
            {
                if (!report.TouchedIds.Contains(c.Id)) continue;
                if (indexById!.TryGetValue(c.Id, out var idx) && idx < Contacts.Count && Contacts[idx].Id == c.Id)
                    Contacts[idx] = c;
                else
                {
                    var live = IndexOfContact(c.Id);
                    if (live >= 0) Contacts[live] = c;
                }
                uiUpdated++;
                if (uiUpdated % 250 == 0 || uiUpdated == touchedTotal)
                {
                    ReportProgress($"Updating list {uiUpdated}/{touchedTotal} contacts", 96 + uiUpdated * 4.0 / touchedTotal);
                    await System.Threading.Tasks.Task.Yield();
                }
            }

            FinishToolImport(
                operation,
                updated: report.ContactsTouched,
                skipped: Math.Max(Contacts.Count - report.ContactsTouched, 0),
                notes: report.Summary);
            _history.Audit("cleanup.run", payload: report.Summary);
            IsBusy = false;
            EndProgress("cleanup");
            RescanDuplicatesCore(userInitiated: false);
            StatusMessage = report.Summary;
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException ex)
        {
            MarkToolImportFailed(operation, ex.Message);
            StatusMessage = $"Cleanup aborted: regex timed out ({ex.Message}).";
        }
        catch (Exception ex)
        {
            MarkToolImportFailed(operation, ex.Message);
            StatusMessage = $"Cleanup failed: {ex.Message}";
            ThemedMessageDialog.Show(Application.Current.MainWindow,
                $"Could not finish cleanup:\n\n{ex.Message}",
                "Cleanup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (IsBusy) IsBusy = false;
            EndProgress("cleanup");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDuplicateResults))]
    private async Task AutoMergeAsync()
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
            var ok = ThemedMessageDialog.Show(Application.Current.MainWindow,
                $"Auto-merge {plan.Plans.Count} group(s)?  Each merge is rollback-able via undo.",
                "Auto-merge",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (ok != MessageBoxResult.OK) return;
        }

        IsBusy = true;
        BeginProgress($"Auto-merging 0/{plan.Plans.Count} groups", indeterminate: false, owner: "auto-merge");
        InvalidateDuplicateScan();
        try
        {
            var merged = 0;
            foreach (var p in plan.Plans)
            {
                // Snapshot primary BEFORE the merge so undo can revert collection-union too.
                var primaryBefore = JsonClone(p.Primary);
                var result = _mergeEngine.Apply(p);
                using var tx = _repo.BeginTransaction();
                _repo.UpdateContact(result.Survivor, tx);
                foreach (var sec in result.RemovedSecondaries) _repo.SoftDeleteContact(sec.Id, tx);
                tx.Commit();
                _history.RecordUndo(
                    "merge",
                    new { primary = result.Survivor.Id, removed = result.RemovedSecondaries.Select(c => c.Id) },
                    new { primaryBefore, restored = result.RemovedSecondaries.Select(c => c.Id) },
                    $"auto-merge {result.Survivor.DisplayName}");
                merged++;
                ReportProgress(
                    $"Auto-merging {merged}/{plan.Plans.Count} groups",
                    merged * 100.0 / plan.Plans.Count);
                if (merged % 25 == 0 || merged == plan.Plans.Count)
                    await System.Threading.Tasks.Task.Yield();
            }
            IsBusy = false;
            EndProgress("auto-merge");
            ReloadFromStore();
            StatusMessage = $"Auto-merged {merged} group(s).  {plan.Skipped} skipped for manual review.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Auto-merge failed: {ex.Message}";
            ThemedMessageDialog.Show(Application.Current.MainWindow,
                $"Could not finish auto-merge:\n\n{ex.Message}",
                "Auto-merge",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (IsBusy) IsBusy = false;
            EndProgress("auto-merge");
        }
    }

    private static void AddCleanupReport(BatchCleanupReport total, BatchCleanupReport partial)
    {
        total.PhonesDeduped += partial.PhonesDeduped;
        total.EmailsDeduped += partial.EmailsDeduped;
        total.UrlsDeduped += partial.UrlsDeduped;
        total.CategoriesDeduped += partial.CategoriesDeduped;
        total.PhonesNormalized += partial.PhonesNormalized;
        total.EmailsCanonicalized += partial.EmailsCanonicalized;
        total.RegexHits += partial.RegexHits;
        total.PhotosStripped += partial.PhotosStripped;
        total.ContactsTouched += partial.ContactsTouched;
        foreach (var id in partial.TouchedIds) total.TouchedIds.Add(id);
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

    /// <summary>Automatic scans above this size are skipped so imports remain recoverable
    /// and tool buttons stay available. The user can still run Rescan explicitly.</summary>
    private const int AutoRescanContactLimit = 25000;

    private int _duplicateScanVersion;

    [RelayCommand(CanExecute = nameof(CanStartDuplicateScan))]
    private void RescanDuplicates()
    {
        RescanDuplicatesCore(userInitiated: true);
    }

    private bool RescanDuplicatesCore(bool userInitiated)
    {
        // Snapshot the input collection so the worker doesn't mutate the UI's
        // ObservableCollection while WPF is iterating it.
        var snapshot = Contacts.ToList();
        var scanVersion = ++_duplicateScanVersion;
        if (snapshot.Count < 2)
        {
            IsDuplicateScanRunning = false;
            EndProgress("scan");
            Duplicates.Clear();
            RebuildDuplicateMembership();
            ContactsView?.Refresh();
            if (userInitiated) StatusMessage = "No duplicates to scan.";
            return true;
        }

        if (!userInitiated && snapshot.Count > AutoRescanContactLimit)
        {
            IsDuplicateScanRunning = false;
            EndProgress("scan");
            Duplicates.Clear();
            RebuildDuplicateMembership();
            ContactsView?.Refresh();
            StatusMessage = $"Duplicate scan skipped for {snapshot.Count} contacts. Use Rescan when ready.";
            return false;
        }

        if (snapshot.Count < AsyncRescanThreshold)
        {
            if (userInitiated)
                BeginProgress($"Scanning {snapshot.Count} contacts", indeterminate: true, owner: "scan");
            IsDuplicateScanRunning = false;
            try
            {
                Duplicates.Clear();
                foreach (var g in _dedup.Find(snapshot)) Duplicates.Add(g);
                RebuildDuplicateMembership();
                ContactsView?.Refresh();
                if (userInitiated) StatusMessage = $"Found {Duplicates.Count} duplicate group(s).";
            }
            finally
            {
                EndProgress("scan");
            }
            return true;
        }

        StatusMessage = $"Scanning {snapshot.Count} contacts for duplicates. Tools remain available.";
        BeginProgress($"Scanning {snapshot.Count} contacts", indeterminate: true, owner: "scan");
        IsDuplicateScanRunning = true;
        _ = System.Threading.Tasks.Task.Run(() => _dedup.Find(snapshot))
            .ContinueWith(t =>
            {
                try
                {
                    if (scanVersion != _duplicateScanVersion) return;
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
                finally
                {
                    if (scanVersion == _duplicateScanVersion)
                    {
                        IsDuplicateScanRunning = false;
                        EndProgress("scan");
                    }
                }
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        return true;
    }

    private void InvalidateDuplicateScan()
    {
        _duplicateScanVersion++;
        if (IsDuplicateScanRunning)
            IsDuplicateScanRunning = false;
        if (_progressOwner == "scan")
            EndProgress("scan");
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
        BeginProgress("Reloading contacts", indeterminate: true, owner: "reload");
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
                finally
                {
                    EndProgress("reload");
                    IsBusy = false;
                }
                // Now that the database gate is open again, kick off the snapshot-based
                // rescan. Large automatic scans will be skipped by RescanDuplicatesCore.
                RescanDuplicatesCore(userInitiated: false);
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
    }

    private int IndexOfContact(Guid id)
    {
        for (int i = 0; i < Contacts.Count; i++)
            if (Contacts[i].Id == id) return i;
        return -1;
    }

    private Dictionary<Guid, int> BuildContactIndexMap()
    {
        var map = new Dictionary<Guid, int>(Contacts.Count);
        for (int i = 0; i < Contacts.Count; i++) map[Contacts[i].Id] = i;
        return map;
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

    private sealed record PendingImportBatch(
        string FilePath,
        ContactSource Source,
        ImportPreviewReport Report);

    private readonly record struct ImportCommitResult(int Added, int Updated, int Skipped);

    private sealed class ImportCommitTotals
    {
        public int FilesCommitted { get; set; }
        public int Added { get; private set; }
        public int Updated { get; private set; }
        public int Skipped { get; private set; }

        public void Add(ImportCommitResult result)
        {
            Added += result.Added;
            Updated += result.Updated;
            Skipped += result.Skipped;
        }
    }
}
