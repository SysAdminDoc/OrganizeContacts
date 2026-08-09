# OrganizeContacts

![OrganizeContacts brand banner](branding/organizecontacts-banner.svg)

[![Version](https://img.shields.io/badge/version-0.3.5-blue.svg)](https://github.com/SysAdminDoc/OrganizeContacts/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-0078d4.svg)](https://github.com/SysAdminDoc/OrganizeContacts)
[![.NET](https://img.shields.io/badge/.NET-10-512bd4.svg)](https://dotnet.microsoft.com/)

OrganizeContacts is a Windows desktop app for importing, reviewing, cleaning, deduplicating, and exporting contact files.

## Supported Workflows

- Import a folder of supported contact files or choose individual files.
- Preview imports before committing changes.
- Import vCard, Google CSV, Outlook CSV, LDIF, jCard, and CardDAV address books.
- Import Android contacts2.db SQLite exports, including groups and embedded photos.
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

## Headless CLI

The `oc` project provides local, scriptable conversion, dedupe, cleanup, and
import commands:

```powershell
dotnet run --project src/OrganizeContacts.Cli -- convert input.vcf output.jcard
dotnet run --project src/OrganizeContacts.Cli -- dedupe input.vcf
dotnet run --project src/OrganizeContacts.Cli -- benchmark 5000 3
```

Conversion also supports `.ics`/`.ical` output for yearly birthday and
anniversary events. Add `--json` to `import`, `convert`/`export`, `dedupe`,
`cleanup`, or `version` for indented machine-readable output.

Crash diagnostics are kept locally at %LOCALAPPDATA%/OrganizeContacts/diagnostics.log;
they are never uploaded or sent as telemetry.

For vCard, CSV, and jCard conversions, the CLI re-imports the output and
reports contact-count and field-level round-trip differences. iCalendar output
is a date-event projection and is not treated as a contact round trip.

## Release artifacts

The local release driver publishes the framework-dependent desktop app and creates
an unsigned MSI, portable zip, CycloneDX SBOM, and SHA-256 manifest:

```powershell
dotnet tool install --global wix --version 5.0.2
pwsh -NoLogo -NoProfile -File packaging/Build-Release.ps1
```

Artifacts are written to `release-artifacts/`. CycloneDX is used when the
`dotnet-CycloneDX` tool is available; otherwise the driver writes the .NET
transitive package manifest as the SBOM fallback. The MSI and executable are
deliberately unsigned in accordance with the repository's no-code-signing policy.
Pass `-CleanArtifacts` when the generated release directory should be reset first.

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
