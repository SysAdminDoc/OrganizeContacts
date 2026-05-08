# Security Policy

## Supported versions

OrganizeContacts is pre-1.0; only the latest minor version receives security
fixes.

| Version | Supported          |
|---------|--------------------|
| 0.3.x   | :white_check_mark: |
| < 0.3   | :x:                |

## Reporting a vulnerability

Use **GitHub Security Advisories**: Repository → Security → Advisories → New
draft security advisory. This routes the report privately to the maintainer
and lets us coordinate a fix before public disclosure.

If GHSA isn't an option for you, email the address listed on the GitHub
profile of [SysAdminDoc](https://github.com/SysAdminDoc) with subject
`[OrganizeContacts security]` and a clear summary. **Do not** report
vulnerabilities by opening a regular GitHub issue.

## What's in scope

- The OrganizeContacts WPF app (`src/OrganizeContacts.App`).
- The `OrganizeContacts.Core` library (parsers, dedup, storage, photos).
- The release artifacts published from `.github/workflows/release.yml`.

## Threat model boundaries

OrganizeContacts is a **single-user local-first desktop tool**. Out-of-scope:

- Multi-user or networked attack surface (the app does not listen on a port,
  does not send data anywhere).
- Vulnerabilities in user-supplied vCard / CSV files exploiting the parser
  *are* in scope — but only if they cause data corruption, RCE, or DoS.
- Cosmetic CSS / theme issues are not security issues.

## Known dependency surface

- `Microsoft.Data.Sqlite` (SQLite for storage)
- `libphonenumber-csharp` (E.164 normalization)
- `CommunityToolkit.Mvvm` (App layer only)

There is currently no image-decoding dependency. PhotoSanitizer walks
JPEG/PNG byte structure directly to strip metadata, capped at 4 MB per photo
(`PhotoSanitizer.MaxPhotoBytes`).

## Disclosure

Once a fix is merged and released, we publish a security advisory referencing
the affected versions and the fix commit. Reporters are credited unless they
prefer to remain anonymous.
