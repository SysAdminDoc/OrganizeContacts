# Changelog

All notable changes to OrganizeContacts will be documented in this file.

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
