# OrganizeContacts

![OrganizeContacts brand banner](branding/organizecontacts-banner.svg)

[![Version](https://img.shields.io/badge/version-0.3.2-blue.svg)](https://github.com/SysAdminDoc/OrganizeContacts/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-0078d4.svg)](https://github.com/SysAdminDoc/OrganizeContacts)
[![.NET](https://img.shields.io/badge/.NET-10-512bd4.svg)](https://dotnet.microsoft.com/)

OrganizeContacts is a Windows desktop app for importing, reviewing, cleaning, deduplicating, and exporting contact files.

## Supported Workflows

- Import a folder of supported contact files or choose individual files.
- Preview imports before committing changes.
- Import vCard, Google CSV, Outlook CSV, LDIF, jCard, and CardDAV address books.
- Review duplicate groups with match reasons and confidence.
- Run cleanup for phone, email, URL, category, photo, and regex-based field cleanup.
- Export contacts to vCard, Google CSV, Outlook CSV, or jCard.
- Restore prior import snapshots and undo merge operations.
- Switch between dark and light themes.

## Current App Status

- Native WPF / .NET 10 desktop shell.
- SQLite storage with import history, audit entries, rollback snapshots, and merge undo.
- MVVM pattern with `CommunityToolkit.Mvvm`.
- Progress reporting for long-running import, cleanup, export, duplicate scan, merge, reload, and clear operations.

## Roadmap (high level)

See [ROADMAP.md](ROADMAP.md) for the full slice plan. Headline goals:

- **v0.2** — vCard 2.1 / 4.0, Google CSV, libphonenumber E.164 normalization, Metaphone + Levenshtein, editable match-weight UI.
- **v0.3** — Side-by-side merge diff, field-level cherry-pick, full undo journal in SQLite, batch normalize (title-case names, strip emoji, expand abbreviations).
- **v0.4** — Outlook PST/OST reader, perceptual-hash photo dedup, Gmail-canonical email matching.
- **v0.5** — iCloud CardDAV sync, Thunderbird MAB import, Android `.vcf` round-trip with photos.
- **v1.0** — Hardened, signed installer, full CardDAV server export.

## Build

```powershell
git clone https://github.com/SysAdminDoc/OrganizeContacts
cd OrganizeContacts
dotnet build -c Release
dotnet run --project src/OrganizeContacts.App
```

Requires .NET 10 SDK on Windows 10 19041 or newer.

## Project Structure

```
OrganizeContacts/
├── src/
│   ├── OrganizeContacts.Core/    # Models, importers, dedup engine, storage
│   └── OrganizeContacts.App/     # WPF MVVM shell
├── branding/                     # Logo prompts and brand assets
└── .github/workflows/            # Release pipeline
```

## License

MIT — see [LICENSE](LICENSE).
