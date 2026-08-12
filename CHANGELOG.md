# Changelog

All notable changes to OrganizeContacts will be documented in this file.

## Unreleased

- Completed RFC 7095 jCard field fidelity for addresses, embedded photos,
  extension properties, preferred phones/emails/addresses, canonical
  multi-value categories, structured names/organizations, and all URLs.
- Added schema migration v2 so preferred-address metadata survives SQLite
  persistence and vCard/jCard round trips.
- Made Google and Outlook CSV exports lossless for the contact model through a
  fingerprint-validated round-trip column that is ignored after visible
  spreadsheet edits.
- Preserved unmodeled CSV columns under their original headers, expanded Google
  address/URL coverage, and recovered legacy Outlook overflow values from Notes.
- Corrected vCard 4 output to use numeric preferences, canonical type tokens,
  and `tel:` URI values; vCard 3 now emits its anniversary extension and
  combined preference types while escaped extensions round-trip exactly.
- Retained unnamed and remote-photo vCards without network access, emitted the
  required name properties, and enforced the 4 MiB raster-photo limit across
  vCard, jCard, CSV metadata, and Android database imports.

## v0.3.5 — 2026-08-09 — Release metadata sync

- Synchronized the application, CLI, manifest, README badge, and in-app
  version to 0.3.5 after draining the active roadmap.

## v0.3.4 — 2026-08-09 — Roadmap completion pass

- Added `--json` output to the headless CLI's import, conversion, dedupe,
  cleanup, and version commands for scriptable local workflows.
- Added iCalendar export for recurring birthday and anniversary events in the
  desktop export dialog and headless conversion command.
- Added a deterministic, dependency-free `oc benchmark` command for measuring
  duplicate detection on synthetic contact pairs.
- Enabled recycling virtualization for contact/group lists and row/column
  virtualization for preview grids to keep large address books responsive.
- Added deterministic malformed-input fuzz coverage and a golden vCard corpus
  round-trip test for parser safety and field retention.
- Added an Android-style vCard 2.1 base64-photo round-trip regression test.
- Added read-only Android contacts2.db SQLite import with names, phones, email,
  addresses, events, groups, photos, and unknown mimetype preservation.
- Added a bounded local diagnostics log for unhandled desktop/task exceptions;
  diagnostic data stays on the machine and is never sent as telemetry.
- Added round-trip field-difference reports to CLI conversion output for vCard,
  CSV, and jCard exports.
- Preserved the numeric values of existing persisted source kinds when adding
  Android contacts database imports.

## v0.3.3 — 2026-08-03 — Release hardening

- Added a reproducible local release driver that creates the framework-dependent
  portable zip, unsigned WiX MSI, CycloneDX SBOM, and SHA-256 manifest.
- Added a Start Menu shortcut and stable upgrade code to the MSI definition.
- Pinned `SQLitePCLRaw.lib.e_sqlite3` to 2.1.12, removing the transitive high-severity
  advisory from the release and warnings-as-errors build.
- Synchronized the application, CLI, manifest, README badge, and in-app version to
  0.3.3.

## v0.3.2 — 2026-05-08 — Deep audit pass

Second hardening sweep, focused on importer correctness, undo fidelity, photo-strip
safety, and CSV export hygiene. 109 tests passing (13 new regression tests guarding
the fixes below).

**Correctness — importers**

- `LdifImporter`: cards exported from Thunderbird/Mozilla MAB with only `mail`,
  `mobile`, `homePhone`, `mozillaWorkStreet` (etc.) are now imported. Pre-fix the
  `seen` flag was set only by `cn`/`givenName`/`sn` AND a stricter top-level gate
  silently dropped any block that lacked one of `cn`/`givenName`/`mail`. Both gates
  now accept any populated person-shaped attribute.
- `JCardImporter`: `CATEGORIES` expressed as a single comma-delimited string
  (`["categories", {}, "text", "vip,client"]`) was unreachable due to a dangling
  `else` binding to an inner `if` — the branch only fired for partial-array inputs
  and the comma-split form silently dropped every category. Braces added so both
  shapes parse. Cards with only `TEL`/`EMAIL`/`URL` no longer dropped (`seen`
  flag now tracks every populated child collection).
- `VCardImporter.SplitStructured`: the structured-field splitter stripped backslash
  escapes before per-leaf `UnescapeText` could see them, so `N:Smith\nJr;…` arrived
  as the literal `nJr` instead of a newline. Now the escape pair survives the split.
- `VCardImporter.Unfold`: the QP soft-line-break unfolder now refuses to swallow a
  following line that lexically begins a fresh vCard property
  (`EMAIL:`/`TEL;…:`/`BEGIN:VCARD`). A malformed QP value ending with `=` could
  previously concat the next property line into the value, hiding `EMAIL`/`TEL`
  fields from import.
- `VCardImporter.TryAttachPhoto`: when a 2.1/3.0 PHOTO arrives with `ENCODING=B`
  but no `TYPE` parameter, the mime is now sniffed from JPEG/PNG/GIF/WebP magic
  bytes instead of left null. Photo bytes are only assigned once the base64 decode
  succeeds — a partial extraction never leaves the contact with bytes-without-mime.
  `Convert.FromBase64String` no longer pays the 3-Replace overhead per photo.

**Correctness — undo**

- Merge undo now restores the survivor to its **pre-merge state** in addition to
  un-soft-deleting the secondaries. Pre-fix the inverse JSON only carried the
  secondary IDs, so undoing a merge restored the secondaries while leaving the
  survivor still holding their merged-in phones/emails/categories — recreating the
  exact duplicates the merge had unified. The new inverse payload (`primaryBefore`)
  is written by both `ReviewMerge` and `AutoMerge`; older entries fall back to the
  legacy secondaries-only restore.

**Security**

- `CsvWriter.Escape`: Excel/Sheets/Numbers formula-injection (CWE-1236) is defanged
  by prefixing a single quote (Excel's literal-text marker) when a cell starts with
  `=`, `+`, `-`, `@`, `\t`, or `\r`. Plain-text and email cells are unaffected.
  Cells that need both the prefix AND quoting (e.g. `=A1,B1`) get both.

**Reliability — robustness & resource safety**

- `PhotoSanitizer.StripJpeg`/`StripPng`: a malformed input (truncated stream,
  bogus segment length, missing IEND) used to produce a half-rewritten output
  that no decoder accepted. Both strippers now return the **original** bytes on
  any structural anomaly so the user keeps a usable file. PNG chunk length is
  range-checked against `int.MaxValue` so an oversized declared length can't
  underflow on the cast.
- `AppSettings.Save`: temp-file uses a per-call random suffix (so two simultaneous
  saves can't race on the same `.tmp`), and the underlying `FileStream`
  flushes-to-disk before the rename so a power-loss between Move and OS commit
  can't surface as a torn settings file.
- `RestoreHistoryDialog`: closing via the title-bar X (or Alt+F4) after a
  successful restore now sets `DialogResult` from `RestorePerformed`, so the
  parent always reloads the contact list. Pre-fix the database had changed but
  the UI list still showed the pre-restore state.

**Performance**

- `DedupEngine`: blocking-bucket membership is now a `HashSet<Guid>` instead of
  `List<Contact>.Contains` — block construction is O(n) again instead of O(n²)
  in the hottest bucket size. A 1000-contact hot bucket no longer eats hundreds
  of milliseconds at scan start.
- `MainViewModel`: import-commit and cleanup now build a single id→index
  `Dictionary` before the `Contacts[idx] = c` loop instead of calling the linear
  `IndexOfContact` per row. For a 10K-row commit landing on a 10K library this
  removed a quadratic UI-thread stall.
- `MainViewModel.ImportFolderAsync`: progress bar no longer stalls when a
  detected file parses to 0 contacts (the `continue` skipped the per-file tick).
- `OpenSettings`: settings-save IO failure no longer crashes the dialog —
  in-memory edits still apply for the session and the user is informed via a
  themed dialog.

## v0.3.1 — 2026-05-07 — Hardening pass

End-to-end correctness, durability, and UX hardening pass across the whole codebase.
93 tests passing (26 new regression tests guarding the fixes below).

**Correctness**

- `DedupEngine`: pair-scoring honours `MatchRules.MinPhoneDigits`. Previously the
  blocking key used the rule but the pair-score was hardcoded to last-7 digits, so a
  Strict profile with `MinPhoneDigits=10` could still match cross-country numbers on
  the trailing 7 digits.
- `VCardImporter`: cards with only `EMAIL`/`TEL`/`PHOTO` properties (no `FN` / `N`)
  are now imported instead of being silently dropped. `CATEGORIES` parsing now
  respects backslash-escaped commas. `CodePagesEncodingProvider` is registered once
  in the type initializer instead of lazily inside a catch block.
- `VCardImporter` line unfolder now handles RFC 2045 quoted-printable soft line
  breaks (a trailing `=` on a QP-encoded line). Pre-fix, long QP-encoded values
  from Outlook for Mac / BlackBerry / older Mozilla exports were silently
  truncated at the first soft line break.
- `VCardWriter`: line folding is now byte-correct (RFC 6350 says 75 octets, not
  chars). Pre-fix, any FN/NOTE/ORG containing CJK or emoji wrote lines >75 octets and
  could split mid-codepoint.
- `BatchCleanup`: `DedupeBy` walks forward and keeps the *first* occurrence (with its
  original kind/IsPreferred metadata). The dead `list.Reverse(); list.Reverse();`
  no-op pair was removed and the keep-last semantics flipped.
- `ImportPreviewer`: REV comparison parses ISO-8601 timestamps before falling back to
  ordinal compare. Previously `"20260301T120000Z"` and `"2026-03-01T12:00:00Z"` were
  treated as different REVs.
- `MergeEngine`: `DateOnly.Parse` of user-chosen birthday/anniversary now uses
  `CultureInfo.InvariantCulture` and accepts multiple ISO formats. Pre-fix this could
  throw `FormatException` on non-US locales mid-merge.
- `MergeEngine`: photo donation — when the surviving primary has no photo, the first
  secondary that does provides it (with its mime type).
- `MergeEngine`: choices for `AdditionalNames`, `HonorificPrefix`, `HonorificSuffix`
  are now applied (previously only the basic name fields, org, title, notes,
  birthday, anniversary).
- `RollbackService.Restore`: uses a new `ContactRepository.ExistsAnyState` check so
  restoring over a soft-deleted row does an UPDATE (with `RestoreContact` to clear
  the tombstone) instead of an INSERT that would fail on the primary-key conflict.
- `ContactRepository.InsertContact` and `UpdateContact` wrap the parent row + child
  `ReplaceChildren` in an implicit transaction when the caller doesn't supply one.
  Previously a SQL failure mid-`ReplaceChildren` could leave the parent row with a
  partial child set; now the whole change rolls back atomically.
- `OutlookCsvWriter` no longer silently drops phone-book overflow. Outlook's CSV
  schema is fixed-width (2 Work, 2 Home, 1 each Mobile/Other/Pager/Main, 1 Business
  Fax + 1 Home Fax) — surplus phones, emails (>3), and URLs (>1) now fold into the
  Notes column with a `[OrganizeContacts overflow]` marker so a follow-up
  Outlook → OrganizeContacts re-import can recover them.  Pre-fix, contacts with
  three work phones or two faxes lost data on every export.

**Security & data safety**

- `CredentialVault`: case-insensitive lookups survive a save→reload cycle.
  `JsonSerializer.Deserialize<Dictionary<…>>` returns an `Ordinal` comparer; we now
  rebuild the dictionary with `OrdinalIgnoreCase` so a credential saved as `"CardDav"`
  is found by `Get("carddav")`.
- `CredentialVault`: corrupt vault files are side-lined as
  `vault.dat.corrupt-<utc>.bak` and a `CorruptVaultDetected` flag is set instead of
  being silently overwritten on the next save.
- `CredentialVault.Persist` and `AppSettings.Save`: atomic writes via `*.tmp` +
  `File.Move(..., overwrite: true)` so a process crash mid-write can't truncate the
  encrypted blob or settings file.
- `AppSettings.LoadOrDefault`: corrupt settings files are side-lined as
  `settings.json.invalid.bak` for inspection (instead of silent reset to defaults).
  Defensive defaults applied for missing region/profile/theme so a partial JSON
  doesn't propagate empty strings into `PhoneNormalizer`.
- `BatchCleanup` regex edits run with a 2-second `RegexMatchTimeoutException` cap so
  a pathological backtracking pattern can't hang the cleanup pipeline. The
  ViewModel surfaces the timeout in the status bar instead of crashing.

**Reliability**

- `CardDavClient` is now `IDisposable` and disposes its owned `HttpClient` (and
  underlying handler). `CardDavConnectDialog` and `CardDavImporter` use `using` so
  per-discovery / per-import sockets aren't leaked. A 60-second `HttpClient.Timeout`
  caps server hangs.
- `GoogleCsvWriter.WriteFileAsync` no longer throws `InvalidOperationException` on
  an empty contact list (`Max()` over empty source) — emits a header-only CSV.
- `MergeReviewDialog` now performs an N-way merge: all secondaries are passed into
  the `MergePlan`, and scalar choices pick the first secondary with a non-empty
  value. Pre-fix, only members[0] and members[1] participated, leaving the rest of a
  3+-contact group untouched until the next rescan.
- `MergeReviewDialog`: `(empty)` placeholder is properly translated back to `null`
  on apply (previously the literal string could be written into the survivor if a
  user ever named a contact `"(empty)"`).
- `CardDavConnectDialog`: validates URL and scheme before dialing, blocks re-entry
  while a discovery is in flight, and shows a wait cursor + button busy state.

**Performance**

- `GoogleCsvImporter`: header lookup is now `O(1)` via a precomputed
  `Dictionary<string, int>` instead of `O(cols)` per `Get` call (which itself was
  called dozens of times per row, producing `O(rows × cols²)` behaviour for large
  exports).
- `BatchCleanup` returns `TouchedIds`, and `MainViewModel.RunCleanup` now persists
  only the rows that actually changed instead of `UPDATE`-ing every contact in the
  database. For a 5,000-row store this turns thousands of writes into typically tens.
- `CardDavImporter` parses incoming vCard bodies in memory via the new
  `VCardImporter.ParseAll(string, string)` entry point — no more temp-file round
  trip per CardDAV card.

**UX**

- `MainWindow`: the "Clear" button is renamed "Clear visible" and now genuinely
  honours the active search/queue filter. Pre-fix, the button advertised
  "Soft-delete all visible contacts" but actually wiped every row in the underlying
  collection — a footgun when the search box was narrowed to a single match.
- `GoogleCsvImporter`: phone-kind classifier matches "WORK FAX" / "OTHER FAX" /
  "BUSINESS" sub-strings instead of relying on exact-string matches against an
  enumeration of Google labels. Also accepts both `"… - Label"` and `"… - Type"`
  header naming for email/phone variants.
- `OpenSettings` rebuilds `_phoneNormalizer`, `_emailCanon`, and `_dedup` when the
  user saves new settings, then triggers an immediate rescan. Pre-fix, region/match
  profile/canonicalization changes were ignored until the next app launch.
- `MainWindow` startup surfaces side-lined corrupt-file recoveries in the status
  bar (settings.json or the credential vault) instead of silently using defaults.

**Storage / SQLite**

- `ContactRepository` opens every connection with `journal_mode=WAL` (non-blocking
  reads while a write is in flight), `synchronous=NORMAL`, `busy_timeout=5000ms`,
  and explicit `foreign_keys=ON`. WAL means a UI-thread `ListContacts` no longer
  stalls behind a background commit.
- `ListContacts` switched from N+1 child queries (5 round trips per contact) to 5
  bucket-by-`contact_id` scans regardless of contact count. For a 5,000-row
  database this drops 25,000 round trips to 5.
- `HistoryStore.RecordUndo` no longer relies on multi-statement `ExecuteScalar`
  to surface `last_insert_rowid()`. The INSERT and the rowid lookup are issued as
  two explicit commands.

**Threading**

- `MainViewModel.RescanDuplicates` runs the dedup engine on a worker thread for
  collections of 500+ contacts (with the result bucketed back onto the UI thread
  via the captured sync context). Below the threshold it stays synchronous so
  small libraries don't pay the context-switch tax.
- `ReloadFromStore` reads the database off the UI thread the same way; the UI
  shows a "Reloading…" status during the round trip.
- New `IsBusy` observable property gates every command that touches the SQLite
  connection (Import / Export / Rescan / Cleanup / AutoMerge / Undo / Clear /
  ReviewMerge / OpenRestoreHistory / OpenSettings) via `[NotifyCanExecuteChangedFor]`.
  Closes the connection-race window where a UI-thread `Audit` call landing
  while the background reload is mid-`ListContacts` could throw `SQLITE_MISUSE`.

**ViewModel performance**

- ContactsView filter switched from `Duplicates × Members` membership lookup
  (O(n²)) to an O(1) `Dictionary<Guid, double>` rebuilt once per dedup pass.
  At 5,000 contacts × 1,000 duplicate groups this was ~5M lookups per filter
  refresh — typed-search now stays interactive.
- `BatchCleanup.Run` accepts a `CancellationToken` so the cleanup pipeline can
  be aborted mid-pass (the future "cancel" button has somewhere to attach).

**WPF chrome**

- `App.ApplyTheme` locates the existing theme dictionary by source path
  instead of assuming `MergedDictionaries[0]`, so a future shared-styles
  dictionary inserted ahead of the theme can't accidentally be overwritten.

**Imports / exports**

- `RunImport`, `ImportCardDavAsync`, and `ExportVCardAsync` are wrapped in
  try/catch with a `MessageBox` fallback. A malformed file or a transient DB
  failure no longer crashes the app or leaves the in-memory ObservableCollection
  ahead of the database — the Contacts list is only mutated after the transaction
  commits.
- Failed imports are recorded with `ImportStatus.Failed` and the exception
  message in the import record's `Notes` so the History pane shows the truth
  instead of a stalled "Pending" row.
- `GoogleCsvImporter`, `OutlookCsvImporter` use `CultureInfo.InvariantCulture`
  for date parsing (Birthday/Anniversary). Pre-fix, an export with `5/7/2026`
  parsed differently on en-US vs en-GB locales.

## v0.3.0 — 2026-05-07 (in-progress)

Format breadth and migration round-trip.

- Added `CsvReader` (RFC 4180-ish, quoted commas/newlines/double-quotes) and `CsvWriter` helpers.
- Added `GoogleCsvImporter`: maps Name/Given Name/Family Name, multi-row "E-mail N", "Phone N", "Address N", "Website N", and Group Membership.
- Added `OutlookCsvImporter`: maps the English Outlook for Windows export schema (3 emails, mobile/home/business/other phones, three address blocks, birthday/anniversary, web page, categories).
- Added `GoogleCsvWriter` and `OutlookCsvWriter` for round-trip export.
- Multi-format export: vCard 3.0 / vCard 4.0 / Google CSV / Outlook CSV from the Save dialog.
- 5 new xunit tests cover CSV import, round-trip, and quoted-field decoding (total: 46 passing).
- Wired `ImportGoogleCsvCommand` + `ImportOutlookCsvCommand` into MainViewModel (preview → snapshot → commit, same as vCard).
- Added `BatchCleanup` service (intra-contact dedupe of phones/emails/URLs/categories, normalize-to-E.164, email canonicalization, regex find/replace across name/org/title/notes/email/phone-raw) and a `CleanupDialog` UI.
- Added `AutoMergeService` (Next#4): picks the richest record as primary, only merges when every secondary is an info-subset and the duplicate group's confidence ≥ AutoMergeThreshold.
- Cleanup runs are rollback-able via an automatically-captured pre-mutation snapshot.
- Added "Cleanup…" and "Auto-merge" header buttons.
- 6 new xunit tests cover BatchCleanup + AutoMergeService (total: 52 passing).
- Added `PhotoSanitizer` (raw byte-walker; no image-decoder dep): strips JPEG `APP1..APP15` + `COM` segments and PNG ancillary chunks (`tEXt`/`iTXt`/`zTXt`/`tIME`/`eXIf`/`gAMA`/`cHRM`/`iCCP`/`sRGB`). 4 MB photo cap. Exposed as a "Strip photo EXIF/metadata" toggle in the Cleanup dialog. 6 new tests.
- Added `CredentialVault` (DPAPI-backed encrypted JSON store; CurrentUser scope; Windows-only). Backing dependency: `System.Security.Cryptography.ProtectedData` 9.0.0. 2 new tests.
- Added live search bar + review queue selector (All / In a duplicate group / Stub / Empty / High confidence) bound to `ContactsView` ICollectionView.
- Added `CONTRIBUTING.md`, `SECURITY.md`, `.github/ISSUE_TEMPLATE/{bug_report,feature_request}.md`.
- Added `.github/workflows/test.yml` CI pipeline: builds with `-warnaserror`, runs xunit, uploads `test-results.trx`, generates dependency manifest + vulnerable-package report on every push and PR.
- Hardened `release.yml`: now runs tests as a gate, builds with `-warnaserror`, attaches an SBOM file (`sbom-vX.Y.Z.txt`) and SHA-256 sums for both ZIP and SBOM.
- Added `docs/MIGRATION_RECIPES.md` covering Google, iCloud, Outlook for Windows, Outlook on the web, Android, and Thunderbird/CardBook export-and-import flows.
- Added `CardDavClient` (Next#6): minimal read-only CardDAV with PROPFIND-based discovery (well-known/.well-known/carddav, current-user-principal, addressbook-home-set), address book listing, and per-card GET with ETag tracking. HttpClient is injectable so tests can mock without a network.
- Added `CardDavImporter` so the same preview/snapshot/UID-REV-idempotence pipeline applies to a CardDAV address book.
- Added `CardDavConnectDialog` UI: server URL + Basic auth credentials + "Save in DPAPI vault" toggle + Discover books + Import selected.
- Bound `ImportCardDavCommand` to a "CardDAV…" header button. Saved credentials prefill on next session via DPAPI vault.
- 3 new xunit tests for CardDavClient parsing + discovery + listing (mocked HttpClient).
- Total tests: 63 passing.
- Added `LdifImporter` (Later#2): reads RFC 2849 v1 LDIF + Mozilla MAB attribute mapping (cn/sn/givenName/o/mail/mozillaSecondEmail/cellPhone/etc.).  2 new tests.
- Added `JCardImporter` and `JCardWriter` (Later#7): RFC 7095 jCard read/write with type/parameter object support.  2 new tests.
- Added "Import LDIF…" and "Import jCard…" header buttons; jCard joins the multi-format Save dialog.
- Added `OrganizeContacts.Cli` project (Later#6): a headless `oc` binary with `convert`, `dedupe`, `cleanup`, `version`, `help` subcommands. Format detection is by extension, including auto-detection between Google CSV and Outlook CSV.
- Added Catppuccin Latte (light theme) ResourceDictionary (F76). Theme picker landed in the Settings dialog; live theme switching via `App.ApplyTheme(string)`.
- Total tests: 67 passing.

## v0.2.0 — 2026-05-07 (in-progress)

Trustworthy local data and vCard. Persisting import results and source attribution.

- Added `OrganizeContacts.Core.Storage.ContactRepository` with SQLite migrations (`schema_version` table, V1 schema).
- Persistent tables: sources, imports, contacts, phones, emails, addresses, urls, categories, audit_log, undo_journal, rollback_snapshots.
- Contact model gained `Uid`, `Rev`, `SourceId`, `ImportId`, `Anniversary`, `CustomFields` (X-* preservation slot), `UpdatedAt`.
- `PhoneNumber.E164` slot for libphonenumber normalization (next).
- `EmailAddress.CanonicalOverride` slot for canonicalization profiles (next).
- `PhoneNumber/EmailAddress/PostalAddress` carry `SourceId` for per-field provenance.
- `ContactSource` and `ImportRecord` models added; UI hydrates from store on launch.
- vCard import is idempotent on UID and uses REV ordering; updates and skips are reported in the status bar.
- `HistoryStore` is now a thin façade over `ContactRepository`; it owns audit + undo journal helpers.
- `ClearAll` is now soft-delete (rollbackable) instead of memory-only.
- Pinned SDK via `global.json` (10.0.202, latestFeature roll-forward).
- Bumped `Microsoft.Data.Sqlite` 9.0.0 → 10.0.7, `CommunityToolkit.Mvvm` 8.4.0 → 8.4.2.
- Added `libphonenumber-csharp` 9.0.18 dependency.
- vCard parser rewritten as a standards-aware reader for **vCard 2.1, 3.0, and 4.0**:
  - VERSION sniff drives encoding decisions (2.1: bare params + CHARSET; 3.0/4.0: backslash text-escapes).
  - RFC 6868 parameter escapes (`^n`, `^^`, `^'`).
  - Quoted-printable + ISO-8859-1 fallback for vCard 2.1 imports (CodePagesEncodingProvider).
  - Embedded base64 photos (3.0 `ENCODING=b;TYPE=jpeg`, 4.0 `data:` URIs).
  - Grouped property syntax (`item1.TEL:...`).
  - X-* custom fields preserved verbatim into `Contact.CustomFields`.
  - Partial vCard 4.0 dates (`--MMDD`).
- Added `VCardWriter` for round-trip export — vCard 3.0 (default) and 4.0 modes, RFC 6350 line folding, escapes both directions.
- Added `OrganizeContacts.Core.Normalize.PhoneNormalizer` (libphonenumber backed, region-configurable, fallback to last-7).
- Added `EmailCanonicalizer` with provider profiles: lowercase, googlemail→gmail, gmail dot-strip, +tag strip across Gmail/FastMail/Proton/iCloud/Outlook.
- Added `NameNormalizer` (diacritic strip, prefix/suffix removal, lightweight Metaphone phonetic key).
- Added `Levenshtein` similarity helper.
- Replaced `DedupEngine` with two-stage matcher: blocking (name/metaphone/E.164/last7/email) + per-pair weighted scoring with explainable `MatchSignal[]`.
- `MatchRules` now carries weights and three named profiles (Default / Strict / Loose).
- `DuplicateGroup` carries `Signals` so the UI can show "matched on phone (+0.45), email (+0.45)".
- Added `ImportPreviewer` for dry-run reports (New / UpdateNewer / SkipUnchanged / SkipOlder / Conflict counts).
- Added `RollbackService` for capturing pre-import snapshots and restoring them.
- Added `AppSettings` (region, match profile, canonicalization toggles, destructive-action confirmations).
- Added `MergeEngine` and `MergePlan` types: scalar field cherry-pick + list-union for phones/emails/addresses/urls/categories/X-* with forward+inverse JSON for the undo journal.
- Added WPF dialogs: `ImportPreviewDialog` (DataGrid of preview items + commit/cancel + snapshot toggle), `SettingsDialog`, `MergeReviewDialog` (side-by-side radio-button cherry-pick), `RestoreHistoryDialog` (imports + snapshots, restore button).
- `MainViewModel` rewired around the new flow: preview-before-commit, snapshot-before-touch, journaled merges, soft-delete clear, undo of last merge.
- `MainWindow` adds Export, Undo, History, Settings, Review&Merge buttons; keyboard shortcuts (Ctrl+O/E/R/Z and Ctrl+,); `AutomationProperties.Name` on every control; live status-bar polite-region for screen readers.
- Added `OrganizeContacts.Tests` (xunit) project with 41 tests covering: vCard 2.1/3.0/4.0 parsing, quoted-printable + UTF-8, X-* preservation, line unfolding, grouped properties, text-escape decoding, vCard writer round-trip + line folding, email canonicalization across providers, name normalization + Metaphone, Levenshtein, libphonenumber wiring, dedup engine signals + threshold profiles + organization-only guardrail, and SQLite repository round-trip + soft delete + UID lookup.
- Tuned `MatchRules.Default.ReviewThreshold` to 0.40 so single strong signals (exact name, phone E.164, email canonical) qualify for review; Strict/Loose profiles unchanged in spirit.
- Phone normalizer now accepts `IsPossibleNumber` so fictional/test 555 numbers are preserved.
- Bug fix: vCard parser no longer eagerly unescapes `\;` before `SplitStructured`, which had been corrupting `ORG`, `N`, and `ADR` fields containing semicolons. UnescapeText now runs on each leaf value after splitting.

## v0.1.0 — 2026-05-07

Initial scaffold release.

- WPF / .NET 10 shell with Catppuccin Mocha theme.
- MVVM via `CommunityToolkit.Mvvm`; sidebar nav (Contacts / Duplicates / Import / Settings).
- `OrganizeContacts.Core` library:
  - `Contact` / `PhoneNumber` / `EmailAddress` / `PostalAddress` / `DuplicateGroup` models.
  - `IContactImporter` contract + `VCardImporter` (vCard 3.0 baseline parser).
  - `DedupEngine` with exact-match strategy on normalized name + phone last-7.
  - SQLite `HistoryStore` scaffold (Microsoft.Data.Sqlite).
- Repo bootstrap: LICENSE (MIT), README with shields.io badges, CHANGELOG, ROADMAP, branding prompts, release workflow.

## Roadmap archive — 2026-08-10 — ROADMAP.md

<details>
<summary>Original roadmap snapshot</summary>

```markdown
# OrganizeContacts Roadmap

Research version: 2026-05-07
Scope: local-first Windows contact organizer, importer, deduper, merge workstation, and reversible cleanup tool.

This roadmap supersedes the original milestone sketch while preserving the shipped v0.1.0 baseline and the project philosophy from the README and local working notes. Every proposed item is traceable to local evidence or an external source in the appendices.

## State of the Repo

### What exists today

- Native WPF desktop shell targeting `net10.0-windows`, with `OrganizeContacts.Core` targeting `net10.0`.
- MIT license, Windows-first release workflow, and a local-first privacy promise: no cloud, no account, no telemetry.
- vCard importer scaffold that reads `BEGIN:VCARD` / `END:VCARD`, unfolds continuation lines, decodes quoted-printable as UTF-8, and maps common 3.0 fields into in-memory `Contact` objects.
- Exact duplicate grouping by normalized display name, phone last 7 digits, and lowercased email.
- SQLite audit/undo schema scaffold; only audit rows are currently written.
- WPF UI for importing one vCard file, viewing contacts, viewing duplicate groups, rescanning, and clearing memory state.

### What is claimed but not implemented

- The README claims import breadth across vCard 2.1/3.0/4.0, Google CSV, Outlook PST, iCloud CardDAV, Android `.vcf`, and Thunderbird MAB. Only baseline `.vcf` import exists.
- The README claims transparent fuzzy rules, side-by-side field diff, field-level merge, and full undo. Current matching is exact and merge UI does not exist.
- The roadmap claims libphonenumber, Metaphone, Levenshtein, photo dedupe, CardDAV, plugin SDK, localization, installer signing, and auto-update. None are present yet.
- Contacts are not persisted; imports are lost when the app closes.

### Hard constraints

- License: MIT for this repo. Roadmap items that introduce AGPL/GPL, commercial SDKs, or non-permissive image libraries require explicit license review before implementation.
- Platform: Windows 10 19041+ today, WPF shell, .NET 10 SDK/runtime.
- Architecture: keep `OrganizeContacts.Core` UI-free so a future Avalonia or CLI shell can reuse import, storage, match, and merge logic.
- Trust model: no cloud processing, no silent telemetry, no destructive merge without preview or undo.

### Repository hygiene gaps

- No test project, no parser corpus, no fuzz/property tests, no issue templates, no `CONTRIBUTING.md`, no `SECURITY.md`, and no `global.json`.
- `Microsoft.Data.Sqlite` is pinned to 9.0.0 while NuGet current stable is 10.0.7; `CommunityToolkit.Mvvm` is pinned to 8.4.0 while current stable is 8.4.2.
- The release workflow only publishes a framework-dependent zip; it does not run tests, sign artifacts, generate an installer, or publish SBOM/checksums beyond SHA-256.

## Strategic Positioning

OrganizeContacts should not try to become a full CRM, cloud address book, or social enrichment service. The defensible lane is narrower and stronger:

- Local-first cleanup for messy exports from many sources.
- Standards-aware import/export with round-trip fidelity and no data loss.
- Transparent duplicate evidence so users can understand and tune matching.
- Reversible merge workflow with dry-run, source attribution, and audit history.
- Power-user cleanup operations that built-in and commercial tools hide or paywall.

## Competitor Snapshot

Snapshot source: GitHub API and public pages on 2026-05-07. "Maintainer signal" lists top contributors or published maintainers when available, not a formal staffing count.

| Project/product | Type | Stars/current signal | Last push/release signal | Maintainer signal | Relevant lesson | Sources |
|---|---:|---:|---:|---|---|---|
| Nextcloud Contacts | OSS web app | 621 stars | pushed 2026-05-07 | Nextcloud Groupware team | Shared address books, app integration, duplicate aggregation request, nested category requests. | S11-S16 |
| Fossify Contacts | OSS Android | 782 stars | release 1.6.0 on 2026-01-30 | Fossify community | Privacy-first mobile contacts need search, groups, export, themes, and sync affordances. | S17 |
| CardBook | OSS Thunderbird add-on | 66k+ users on Thunderbird add-ons | version 102.4 on 2025-12-04 | Philippe V. | vCard/CardDAV depth, categories, duplicate merge, Gmail tags, photos. | S18, S19 |
| Duplicate Contacts Manager | OSS Thunderbird add-on | 25 stars | pushed 2026-04-19 | DDvO, stefmorp | Best direct evidence for side-by-side field comparison, match explanations, subset delete, and configurable ignored fields. | S20 |
| kontakt-schnabel | OSS CLI | 0 stars but recent | pushed 2026-03-22 | single maintainer | Strong local pipeline: import, classify, normalize, sanitize, match, dedup, export, undo, SQLite sessions, tests. | S22 |
| vcardtools | OSS CLI | 59 stars | pushed 2024-11-08 | mbideau plus contributors | vCard 2.1->3.0 conversion, fuzzy matching options, field fixes, functional test corpus. | S21 |
| khard | OSS CLI | 662 stars | pushed 2026-04-29 | lucc plus contributors | vCard interoperability is fragile; read-only workflows are safer across Android/iOS. | S23 |
| vdirsyncer | OSS CLI sync | 1818 stars | pushed 2026-04-07 | pimutils community | Local vdir storage plus server sync is a proven offline-first contact/calendar pattern. | S24 |
| Radicale | OSS CardDAV server | 4630 stars | v3.7.2 on 2026-04-29 | multiple maintainers | Small plugin-extensible CardDAV server, filesystem storage, TLS/auth/access-control patterns. | S25 |
| Baikal | OSS CardDAV server | 3149 stars | pushed 2026-05-02 | volunteer maintainers | Lightweight CardDAV server on sabre/dav; upgrade docs and password hashing issue are roadmap signals. | S26 |
| Monica | OSS personal CRM | 24616 stars | pushed 2026-04-24 | two core maintainers plus community | Contacts can grow into relationships, notes, labels, reminders, multi-user, i18n; most is out of scope for v1. | S27 |
| Cardamum | OSS CLI | 23 stars | pushed 2026-02-24 | Pimalaya | CardDAV/Vdir CLI with JSON output, keyring/command credential storage, OAuth configuration. | S28 |
| EteSync DAV | OSS sync bridge | 338 stars | pushed 2026-01-08 | EteSync community | Local DAV adapter, localhost UI, data dirs, autostart, OS-specific client setup, signing requests. | S29 |
| DAVx5 | OSS/commercial Android sync | public commercial app | active docs | DAVx5 team | Wide field support, vCard3 categories, vCard4 groups, photos, shared read-only books, WebDAV Push. | S30, S50 |
| Contacts+ | Commercial SaaS | paid tiers | docs updated 2025 | vendor | Duplicates, cleanup, backups/history, updates, sync limits, and automation are premium features. | S31, S32 |
| Covve | Commercial SaaS | paid tiers | active pricing | vendor | Scanning, groups, notes, exports, CRM integrations, AI lead research are monetized. | S33 |
| CopyTrans Contacts | Commercial desktop | active guides | updated 2025 | vendor | Device/cloud import/export breadth and PC editing are high-value desktop workflows. | S34 |
| Cisdem ContactsMate | Commercial desktop | active guide | updated 2025 | vendor | Account-level duplicate scans, conflict groups, "Fix All", and trash-based recovery. | S35 |
| Google/Apple/Outlook built-ins | Platform tools | bundled | current help docs | platform vendors | Merge/fix exists but is opaque, limited across accounts, and often not truly destructive merge. | S05-S10 |

## Decision Framework

Impact:

- 5 = central to trust, data retention, or core dedupe value.
- 4 = frequently requested parity or high workflow leverage.
- 3 = valuable once core workflow exists.
- 2 = niche or mostly developer/operator value.
- 1 = not aligned or low user value.

Effort:

- 1 = small local code/docs change.
- 2 = contained module plus tests.
- 3 = multi-module feature.
- 4 = substantial architecture or UX surface.
- 5 = high complexity, sync/protocol/security/legal risk.

Tiers:

- Now: should land before the next credible public release.
- Next: follows once persistence, parser fidelity, and merge safety are stable.
- Later: useful, but not on the critical path to a trustworthy deduper.
- Under Consideration: possible, but needs research, dependency validation, or demand proof.
- Rejected: contradicts local-first scope, license posture, or trust model.

## Prioritized Roadmap

### Now

### Next

### Later

### Under Consideration

## Rejected

| Idea | Sources | Reason |
|---|---|---|
| Default cloud upload for matching or enrichment | L01, S31-S33 | Contradicts the core local-first promise. |
| Silent telemetry or contact analytics | L01, S27 | Contradicts the no-telemetry trust model. |
| Irreversible bulk merge/delete | S20, S22, S32, S35 | Directly conflicts with reversible cleanup and user trust. |
| Using AGPL/GPL code inside the MIT core without isolation | S25-S27 | License mismatch; can be studied but not embedded casually. |
| Commercial PST SDK by default without a fallback path | L02, S36 | Licensing and redistribution risk; evaluate only behind an abstraction. |
| Social scraping of LinkedIn/GitHub by default | L02, S31-S33 | Privacy, terms, and scope risk; keep any enrichment local and opt-in. |
| Full CRM relationship management before v1 | S27 | Valuable in Monica, but it dilutes the dedupe/import mission. |
| Mobile write-sync before desktop merge safety | S30, S50 | Multiplies conflict risk before local model and undo are trustworthy. |
| AI-generated contact updates from the web | S31-S33 | Cloud dependence and hallucination risk are misaligned with reliable cleanup. |
| Auto-deleting low-information contacts without preview | S20, S22, S35 | Even "empty" or "stub" contacts can contain user-meaningful context. |

## Raw Feature Harvest and Prioritization

Abbreviations: I = impact, E = effort, R = risk, N = novelty. Fit is "Y", "N", or "Maybe".

| ID | Feature | Category | Prevalence | Sources | Fit | I | E | R | Depends on | N | Tier | Placement reason |
|---|---|---|---|---|---:|---:|---:|---|---|---|---|---|

## Status snapshot (2026-05-07)

- **Now tier (12 / 12)** — shipped in v0.2.0.
- **Next tier (10 / 10)** — shipped in v0.3.0.
- **Later tier (3 / 10)** — LDIF (#2), CLI (#6), jCard (#7) shipped in v0.3.0; the remaining seven are explicitly deferred to v0.4 with the rationale recorded inline.
- **Under Consideration / Rejected** — unchanged; the active-learning scorer, business-card OCR, multi-user collab, WebDAV Push, and social enrichment remain gated by the criteria stated in this file.

The Release 0.2 + 0.3 goals from the Delivery Sequence below have both shipped.

## Delivery Sequence

### Release 0.2 - Trustworthy local data and vCard

- Persistent contacts/sources/imports with migrations.
- Parser/writer decision and implementation for vCard 2.1/3.0/4.0.
- Golden corpus and parser fuzz/property tests.
- Import preview, dry-run report, UID/REV idempotence, rollback snapshot.
- Source/account attribution in data model and UI.
- libphonenumber normalization, email canonicalization, and name normalization.

### Release 0.3 - Transparent duplicate review

- Blocking and weighted duplicate scoring.
- Match explanations and threshold profiles.
- Side-by-side merge UI with field cherry-pick.
- Undo journal, audit viewer, and restore.
- Accessibility pass for keyboard, focus, screen reader names, high contrast basics.

### Release 0.4 - Batch cleanup and export

- Intra-contact field dedupe and sanitize commands.
- Batch normalize, regex edit, saved filters, and review queues.
- Google/Outlook CSV importers and custom mapping.
- vCard/CSV export with round-trip tests and export report.
- Local diagnostics, source-specific migration docs, and OSS contribution docs.

### Release 0.5 - Photos and sync sources

- Photo parse/preserve/export, EXIF stripping, size limits, and optional perceptual hash matching.
- CardDAV read-only import with discovery, ETags, credential vault, and conflict-safe local snapshots.
- Android `.vcf` photo round-trip and Thunderbird/CardBook migration helpers if core vCard coverage is stable.

### Release 1.0 - Hardened Windows release

- Installer plus portable zip.
- Authenticode signing, SBOM, dependency scan, checksums, release notes, and upgrade guide.
- Performance benchmarks for large address books.
- Stable plugin-facing importer/exporter abstractions, but no public plugin SDK until contracts are proven.

## Category Coverage Audit

- Security: credential vault, image parser limits, dependency scanning, SBOM, signed releases, security policy.
- Accessibility: keyboard merge flow, screen reader names, high contrast, destructive action confirmations.
- i18n/l10n: region-aware phone parsing now; UI localization later.
- Observability/telemetry: local audit/history/diagnostics only; telemetry rejected.
- Testing: parser corpus, fuzz tests, unit/integration tests, benchmarks.
- Docs: migration recipes, rule explanations, contributing/security docs.
- Distribution/packaging: installer, portable zip, signing, checksums, SBOM, upgrade notes.
- Plugin ecosystem: later, after core interfaces stabilize.
- Mobile: Android `.vcf` round-trip next; mobile companion later.
- Offline/resilience: local-first persistence, snapshots, undo, no cloud matching.
- Multi-user/collab: under consideration only after LAN/CardDAV server work.
- Migration paths: vCard, CSV, CardDAV, Outlook, Thunderbird, Android.
- Upgrade strategy: migrations, `global.json`, package update policy, release workflow hardening.

## Self-Audit

- Every roadmap item references local evidence or source IDs listed below.
- Rejected items are explicit and do not reappear in accepted tiers.
- The Now tier is limited to prerequisites for trustworthy local import, matching, merge, undo, and verification.
- High-risk dependency areas are isolated: PST/OST, image processing, CardDAV write sync, cloud enrichment, and plugins.
- The roadmap preserves the project philosophy: offline by default, format breadth, transparent fuzzy matching, reversible merge.
- `ROADMAP.md` is located at the repository root.

## Appendix A - Local Evidence

| ID | Evidence |
|---|---|
| L01 | `README.md` - project philosophy, claimed differentiators, current feature list. |
| L02 | previous `ROADMAP.md` - original milestone sketch. |
| L03 | `CHANGELOG.md` - v0.1.0 shipped scaffold summary. |
| L04 | `src/OrganizeContacts.App/OrganizeContacts.App.csproj` and `src/OrganizeContacts.Core/OrganizeContacts.Core.csproj` - .NET targets and package pins. |
| L05 | `src/OrganizeContacts.Core/**` and `src/OrganizeContacts.App/ViewModels/MainViewModel.cs` - actual parser, dedup, storage, UI behavior. |
| L06 | `.github/workflows/release.yml` - current release packaging. |

## Appendix B - External Sources

| ID | URL |
|---|---|
| S01 | https://www.rfc-editor.org/rfc/rfc6350 |
| S02 | https://www.rfc-editor.org/rfc/rfc6352 |
| S03 | https://www.rfc-editor.org/rfc/rfc7095 |
| S04 | https://www.rfc-editor.org/rfc/rfc9553 |
| S05 | https://support.apple.com/guide/iphone/merge-or-hide-duplicate-contacts-iph2ab28320d/ios |
| S06 | https://support.apple.com/en-au/guide/contacts/adrbk1498/mac |
| S07 | https://support.google.com/contacts/answer/7078226 |
| S08 | https://support.google.com/contacts/answer/7199294 |
| S09 | https://support.microsoft.com/office/import-contacts-to-outlook-for-windows-bb796340-b58a-46c1-90c7-b549b8f3c5f8 |
| S10 | https://developer.thunderbird.net/thunderbird-development/codebase-overview/address-book |
| S11 | https://github.com/nextcloud/contacts |
| S12 | https://github.com/nextcloud/contacts/issues/5246 |
| S13 | https://github.com/nextcloud/contacts/issues/5192 |
| S14 | https://github.com/nextcloud/contacts/issues/5191 |
| S15 | https://github.com/nextcloud/contacts/issues/5245 |
| S16 | https://github.com/nextcloud/contacts/issues/5277 |
| S17 | https://github.com/FossifyOrg/Contacts |
| S18 | https://services.addons.thunderbird.net/EN-us/thunderbird/addon/cardbook/ |
| S19 | https://gitlab.com/CardBook |
| S20 | https://github.com/DDvO/Duplicate-Contacts-Manager |
| S21 | https://github.com/mbideau/vcardtools |
| S22 | https://github.com/cedi-ch/kontakt-schnabel |
| S23 | https://github.com/lucc/khard |
| S24 | https://github.com/pimutils/vdirsyncer |
| S25 | https://github.com/Kozea/Radicale |
| S26 | https://github.com/sabre-io/Baikal |
| S27 | https://github.com/monicahq/monica |
| S28 | https://github.com/pimalaya/cardamum |
| S29 | https://github.com/etesync/etesync-dav |
| S30 | https://www.davx5.com/ |
| S31 | https://support.contactsplus.com/hc/en-us/articles/22538374387611-Contacts-Premium-Trial |
| S32 | https://support.contactsplus.com/hc/en-us/articles/4407278226459-The-Assistant-Applying-Updates-and-Duplicates |
| S33 | https://covve.com/pricing |
| S34 | https://www.copytrans.net/support/user-guides-copytrans-contacts/ |
| S35 | https://www.cisdem.com/resource/cisdem-contactsmate-mac-advanced-guide.html |
| S36 | https://imazing.com/documentation/iMazing-CLI-Documentation.pdf |
| S37 | https://play.google.com/store/apps/details?id=com.forteam.mergix |
| S38 | https://www.reddit.com/r/SimpleMobileTools/comments/ugqikb |
| S39 | https://www.reddit.com/r/iphone/comments/13ftk6b |
| S40 | https://www.reddit.com/r/macapps/comments/1okjfir |
| S41 | https://www.reddit.com/r/Thunderbird/comments/1ahpccw |
| S42 | https://www.reddit.com/r/openphone/comments/1jbajmm |
| S43 | https://stackoverflow.com/questions/50655191/how-do-i-detect-changes-in-vcards |
| S44 | https://apple.stackexchange.com/questions/433906/how-can-i-import-a-vcard-without-getting-duplicated-fields |
| S45 | https://forum.mudita.com/t/contacts-duplicated-missing-labels-after-importing-from-vcard/12466 |
| S46 | https://correctvcf.com/help/fix-duplicate-contacts-vcf-import/ |
| S47 | https://github.com/rwsturm/awesome-selfhosted |
| S48 | https://docs.kde.org/stable_kf6/en/kaddressbook/kaddressbook/ |
| S49 | https://wiki.gnome.org/Apps%282f%29Contacts.html |
| S50 | https://manual.davx5.com/introduction.html |
| S51 | https://github.com/dedupeio/dedupe |
| S52 | https://docs.dedupe.io/ |
| S53 | https://fritshermans.github.io/posts/Deduplipy.html |
| S54 | https://github.com/google/libphonenumber/blob/master/FALSEHOODS.md |
| S55 | https://github.com/twcclegg/libphonenumber-csharp |
| S56 | https://www.nuget.org/packages/FolkerKinzel.VCards |
| S57 | https://github.com/mixerp/MixERP.Net.VCards |
| S58 | https://github.com/Aptivi/VisualCard |
| S59 | https://github.com/coenm/ImageHash |
| S60 | https://github.com/advisories/GHSA-2cmq-823j-5qj8 |
| S61 | https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.7 |
| S62 | https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2 |
| S63 | https://devblogs.microsoft.com/dotnet/dotnet-10-0-7-oob-security-update/ |
| S64 | https://nvd.nist.gov/vuln/detail/CVE-2025-6965 |
| S65 | https://github.com/sabre-io/dav |
| S66 | https://github.com/natelindev/tsdav |
| S67 | https://bugzilla.mozilla.org/show_bug.cgi?id=2013764 |
| S68 | https://github.com/topics/contact-manager |
| S69 | https://www.rfc-editor.org/rfc/rfc6868 |
| S70 | https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100 |
```

</details>
