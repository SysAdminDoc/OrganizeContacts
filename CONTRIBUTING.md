# Contributing to OrganizeContacts

Thanks for your interest. OrganizeContacts is a single-user, local-first Windows
desktop deduper. Contributions are welcome — this guide covers what fits the
roadmap and how to run the build locally.

## Scope

OrganizeContacts is a **deduper and migration tool**, not a CRM, not a sync
server, not a contact-enrichment service. Pull requests that conflict with the
local-first / no-cloud / no-telemetry posture will be closed. See
[ROADMAP.md](ROADMAP.md) for the full prioritization framework and the
[Rejected](ROADMAP.md#rejected) table.

If you want to land a feature that isn't on the roadmap, open a discussion or
issue first so we can sanity-check the impact/effort/risk before you invest.

## Getting started

Requirements:

- Windows 10 19041+ (UI is WPF; `OrganizeContacts.Core` is portable).
- .NET 10 SDK (pinned via `global.json`; `latestFeature` roll-forward).

```powershell
git clone https://github.com/SysAdminDoc/OrganizeContacts.git
cd OrganizeContacts
dotnet build -c Release
dotnet test
dotnet run --project src/OrganizeContacts.App
```

## Architecture

- `src/OrganizeContacts.Core/` — UI-free library. Models, importers/writers,
  normalize, dedup, merge, storage, photos. Reused by future Avalonia/CLI shells.
- `src/OrganizeContacts.App/` — WPF shell, MVVM, `CommunityToolkit.Mvvm`.
  Catppuccin Mocha theme.
- `tests/OrganizeContacts.Tests/` — xunit, no GUI. New non-trivial logic
  belongs here.

## Coding conventions

- C# 13 / .NET 10 / `LangVersion=latest` / `Nullable` enabled.
- No third-party deps in `Core` without roadmap justification (license, size,
  CVE history).
- Public types/methods get a one-line `<summary>` comment that explains
  *why* they exist. WHAT the code does should be obvious from the name.
- No emoji or unicode glyphs in code, comments, or commit messages.
- Tests must accompany any change to importers, normalizers, dedup, merge, or
  storage.

## Pull request checklist

- [ ] `dotnet build -c Release` succeeds with 0 errors / 0 warnings.
- [ ] `dotnet test` passes locally.
- [ ] New behavior has tests in `tests/OrganizeContacts.Tests/`.
- [ ] `CHANGELOG.md` lists the change under the unreleased section.
- [ ] No telemetry, no cloud calls (or, if needed, behind explicit user opt-in
      with a privacy review in the PR description).
- [ ] If user-visible: `AutomationProperties.Name` set on every interactive
      control so screen readers + Windows Narrator can announce the action.

## Reporting bugs

Use the GitHub issue templates in `.github/ISSUE_TEMPLATE/`. For
data-corruption-class bugs (lost contacts, broken merge, stuck rollback),
include:

- The OS version and `dotnet --info` output.
- The vCard / CSV file that triggered it (or a synthetic minimal repro).
- The contents of `%LocalAppData%\OrganizeContacts\contacts.sqlite` if it's
  safe to share (it contains your address book — sanitize first).

## Security

See [SECURITY.md](SECURITY.md). Report vulnerabilities privately via GitHub
Security Advisories before opening a public issue.

## License

By contributing you agree your work is licensed under the MIT License (see
[LICENSE](LICENSE)).
