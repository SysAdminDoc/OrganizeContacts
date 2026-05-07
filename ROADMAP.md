# OrganizeContacts — Roadmap

## v0.1.0 — 2026-05-07 (shipped)
- [x] Repo bootstrap, LICENSE, README, CHANGELOG, branding prompts
- [x] WPF / .NET 10 shell with Catppuccin Mocha theme
- [x] MVVM scaffolding (`CommunityToolkit.Mvvm`)
- [x] `Contact` model + `VCardImporter` (vCard 3.0)
- [x] Exact-match `DedupEngine`
- [x] SQLite `HistoryStore` scaffold
- [x] Release workflow (`workflow_dispatch`)

## v0.2.0 — Format breadth + fuzzy match foundation
- [ ] vCard 2.1 + 4.0 parsers (full RFC 6350 compliance)
- [ ] Google Contacts CSV importer (3 formats: Outlook, vCard, Google CSV variants)
- [ ] `libphonenumber-csharp` integration → E.164 normalization
- [ ] Metaphone phonetic algorithm (`OrganizeContacts.Core/Dedup/Metaphone.cs`)
- [ ] Levenshtein edit distance with cached similarity matrix
- [ ] Configurable match-weight rules (UI exposes the slider)
- [ ] Email canonicalization (Gmail dot/plus stripping)
- [ ] Persist imported contacts to SQLite, not just in-memory

## v0.3.0 — Visual merge UX + undo journal
- [ ] Side-by-side merge diff view with field-level cherry-pick
- [ ] Per-field source attribution (which import produced this email?)
- [ ] Full undo journal — every merge / split / edit recorded in SQLite
- [ ] Replay log: `Ctrl+Z` undoes the last operation, including merges
- [ ] Batch normalize commands: title-case names, strip emoji, expand abbreviations (`St.` → `Street`)
- [ ] Bulk regex find/replace across selected fields
- [ ] Export back to vCard / Google CSV with full round-trip fidelity

## v0.4.0 — Outlook + photos
- [ ] Outlook PST/OST reader (`Aspose.Email` evaluation vs. `OutlookStorage` OSS)
- [ ] Outlook MSG single-contact import
- [ ] Photo dedup via perceptual hash (`CoenM.ImageHash`)
- [ ] Photo normalization (resize 1024px max, strip EXIF, prefer highest-res source)
- [ ] Match weight: photo perceptual-hash similarity

## v0.5.0 — Cloud-free sync sources
- [ ] iCloud CardDAV read-only client (local credentials, no cloud relay)
- [ ] Thunderbird MAB import (Mork format)
- [ ] Android `.vcf` photo round-trip (BASE64 inline photos)
- [ ] Multi-account merge (shows source account per contact)

## v0.6.0 — Power-user batch ops
- [ ] Saved query / saved filter library
- [ ] Bulk title formatter (job titles → consistent capitalization)
- [ ] Country-aware address normalization (`USPS Web Tools` optional, opt-in)
- [ ] Birthday / anniversary detection from notes field
- [ ] Tag / group bulk operations

## v1.0.0 — Hardened release
- [ ] Signed installer (Inno Setup + Authenticode cert)
- [ ] Auto-update channel
- [ ] CardDAV server mode (publish your contacts on LAN, no cloud)
- [ ] Plugin SDK (third-party importers)
- [ ] Localization framework (en, de, es, fr, ja first)

## Stretch / later
- [ ] macOS port (Avalonia UI re-shell sharing `OrganizeContacts.Core`)
- [ ] Mobile companion app (read-only viewer that pulls from desktop over LAN)
- [ ] LinkedIn / GitHub social-handle enrichment (offline cache only)
