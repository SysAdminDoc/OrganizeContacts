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
- Added `libphonenumber-csharp` 9.0.18 dependency (wired up in next batch).

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
