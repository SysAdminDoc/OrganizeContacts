# Changelog

All notable changes to OrganizeContacts will be documented in this file.

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
