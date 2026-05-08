# Changelog

All notable changes to OrganizeContacts will be documented in this file.

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
