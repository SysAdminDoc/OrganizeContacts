# OrganizeContacts

[![Version](https://img.shields.io/badge/version-0.1.0-blue.svg)](https://github.com/SysAdminDoc/OrganizeContacts/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-0078d4.svg)](https://github.com/SysAdminDoc/OrganizeContacts)
[![.NET](https://img.shields.io/badge/.NET-10-512bd4.svg)](https://dotnet.microsoft.com/)

**Local-first contact organizer + deduper.** Your contacts never leave your machine.

OrganizeContacts is a native Windows desktop app that imports contacts from every common format (vCard 2.1/3.0/4.0, Google CSV, Outlook PST, iCloud CardDAV, Android `.vcf`, Thunderbird MAB), finds duplicates with transparent fuzzy rules you can tune, and merges them with a side-by-side field-level diff and a full undo journal.

## Why OrganizeContacts

Every premium contact deduper (Scrubly, Contacts+, Covve) requires uploading your address book to their cloud. Every OSS option (Fossify, GNOME Contacts, CardBook) only catches exact-match duplicates and skips phone normalization, photo dedup, and PST entirely. OrganizeContacts is built on three principles:

1. **Offline by default.** No cloud, no telemetry, no account.
2. **Format breadth.** One app for everything you've exported from every device you own.
3. **Transparent fuzzy match.** You see *why* two contacts are flagged as duplicates, and you can edit the weights.

## Features (v0.1.0 — initial scaffold)

- Native WPF / .NET 10 desktop shell, Catppuccin Mocha dark theme.
- vCard 3.0 importer (foundation — 2.1 / 4.0 in v0.2).
- Exact-match duplicate detection on normalized name + phone (fuzzy/Metaphone in v0.2).
- SQLite-backed audit log scaffold (full undo journal in v0.3).
- MVVM pattern with `CommunityToolkit.Mvvm`.

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
