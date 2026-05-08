# OrganizeContacts Roadmap

Research version: 2026-05-07
Scope: local-first Windows contact organizer, importer, deduper, merge workstation, and reversible cleanup tool.

This roadmap supersedes the original milestone sketch while preserving the shipped v0.1.0 baseline and the project philosophy from the README and local working notes. Every proposed item is traceable to local evidence or an external source in the appendices.

## State of the Repo

### What exists today

- Native WPF desktop shell targeting `net10.0-windows`, with `OrganizeContacts.Core` targeting `net10.0`.
- MIT license, Windows-first release workflow, and a local-first privacy promise: no cloud, no account, no telemetry.
- vCard importer scaffold that reads `BEGIN:VCARD` / `END:VCARD`, unfolds continuation lines, decodes quoted-printable as UTF-8, and maps common 3.0 fields into in-memory `Contact` objects.
- Exact duplicate grouping by normalized display name, phone last 7 digits, and lowercased email.
- SQLite audit/undo schema scaffold; only audit rows are currently written.
- WPF UI for importing one vCard file, viewing contacts, viewing duplicate groups, rescanning, and clearing memory state.

### What is claimed but not implemented

- The README claims import breadth across vCard 2.1/3.0/4.0, Google CSV, Outlook PST, iCloud CardDAV, Android `.vcf`, and Thunderbird MAB. Only baseline `.vcf` import exists.
- The README claims transparent fuzzy rules, side-by-side field diff, field-level merge, and full undo. Current matching is exact and merge UI does not exist.
- The roadmap claims libphonenumber, Metaphone, Levenshtein, photo dedupe, CardDAV, plugin SDK, localization, installer signing, and auto-update. None are present yet.
- Contacts are not persisted; imports are lost when the app closes.

### Hard constraints

- License: MIT for this repo. Roadmap items that introduce AGPL/GPL, commercial SDKs, or non-permissive image libraries require explicit license review before implementation.
- Platform: Windows 10 19041+ today, WPF shell, .NET 10 SDK/runtime.
- Architecture: keep `OrganizeContacts.Core` UI-free so a future Avalonia or CLI shell can reuse import, storage, match, and merge logic.
- Trust model: no cloud processing, no silent telemetry, no destructive merge without preview or undo.

### Repository hygiene gaps

- No test project, no parser corpus, no fuzz/property tests, no issue templates, no `CONTRIBUTING.md`, no `SECURITY.md`, and no `global.json`.
- `Microsoft.Data.Sqlite` is pinned to 9.0.0 while NuGet current stable is 10.0.7; `CommunityToolkit.Mvvm` is pinned to 8.4.0 while current stable is 8.4.2.
- The release workflow only publishes a framework-dependent zip; it does not run tests, sign artifacts, generate an installer, or publish SBOM/checksums beyond SHA-256.

## Strategic Positioning

OrganizeContacts should not try to become a full CRM, cloud address book, or social enrichment service. The defensible lane is narrower and stronger:

- Local-first cleanup for messy exports from many sources.
- Standards-aware import/export with round-trip fidelity and no data loss.
- Transparent duplicate evidence so users can understand and tune matching.
- Reversible merge workflow with dry-run, source attribution, and audit history.
- Power-user cleanup operations that built-in and commercial tools hide or paywall.

## Competitor Snapshot

Snapshot source: GitHub API and public pages on 2026-05-07. "Maintainer signal" lists top contributors or published maintainers when available, not a formal staffing count.

| Project/product | Type | Stars/current signal | Last push/release signal | Maintainer signal | Relevant lesson | Sources |
|---|---:|---:|---:|---|---|---|
| Nextcloud Contacts | OSS web app | 621 stars | pushed 2026-05-07 | Nextcloud Groupware team | Shared address books, app integration, duplicate aggregation request, nested category requests. | S11-S16 |
| Fossify Contacts | OSS Android | 782 stars | release 1.6.0 on 2026-01-30 | Fossify community | Privacy-first mobile contacts need search, groups, export, themes, and sync affordances. | S17 |
| CardBook | OSS Thunderbird add-on | 66k+ users on Thunderbird add-ons | version 102.4 on 2025-12-04 | Philippe V. | vCard/CardDAV depth, categories, duplicate merge, Gmail tags, photos. | S18, S19 |
| Duplicate Contacts Manager | OSS Thunderbird add-on | 25 stars | pushed 2026-04-19 | DDvO, stefmorp | Best direct evidence for side-by-side field comparison, match explanations, subset delete, and configurable ignored fields. | S20 |
| kontakt-schnabel | OSS CLI | 0 stars but recent | pushed 2026-03-22 | single maintainer | Strong local pipeline: import, classify, normalize, sanitize, match, dedup, export, undo, SQLite sessions, tests. | S22 |
| vcardtools | OSS CLI | 59 stars | pushed 2024-11-08 | mbideau plus contributors | vCard 2.1->3.0 conversion, fuzzy matching options, field fixes, functional test corpus. | S21 |
| khard | OSS CLI | 662 stars | pushed 2026-04-29 | lucc plus contributors | vCard interoperability is fragile; read-only workflows are safer across Android/iOS. | S23 |
| vdirsyncer | OSS CLI sync | 1818 stars | pushed 2026-04-07 | pimutils community | Local vdir storage plus server sync is a proven offline-first contact/calendar pattern. | S24 |
| Radicale | OSS CardDAV server | 4630 stars | v3.7.2 on 2026-04-29 | multiple maintainers | Small plugin-extensible CardDAV server, filesystem storage, TLS/auth/access-control patterns. | S25 |
| Baikal | OSS CardDAV server | 3149 stars | pushed 2026-05-02 | volunteer maintainers | Lightweight CardDAV server on sabre/dav; upgrade docs and password hashing issue are roadmap signals. | S26 |
| Monica | OSS personal CRM | 24616 stars | pushed 2026-04-24 | two core maintainers plus community | Contacts can grow into relationships, notes, labels, reminders, multi-user, i18n; most is out of scope for v1. | S27 |
| Cardamum | OSS CLI | 23 stars | pushed 2026-02-24 | Pimalaya | CardDAV/Vdir CLI with JSON output, keyring/command credential storage, OAuth configuration. | S28 |
| EteSync DAV | OSS sync bridge | 338 stars | pushed 2026-01-08 | EteSync community | Local DAV adapter, localhost UI, data dirs, autostart, OS-specific client setup, signing requests. | S29 |
| DAVx5 | OSS/commercial Android sync | public commercial app | active docs | DAVx5 team | Wide field support, vCard3 categories, vCard4 groups, photos, shared read-only books, WebDAV Push. | S30, S50 |
| Contacts+ | Commercial SaaS | paid tiers | docs updated 2025 | vendor | Duplicates, cleanup, backups/history, updates, sync limits, and automation are premium features. | S31, S32 |
| Covve | Commercial SaaS | paid tiers | active pricing | vendor | Scanning, groups, notes, exports, CRM integrations, AI lead research are monetized. | S33 |
| CopyTrans Contacts | Commercial desktop | active guides | updated 2025 | vendor | Device/cloud import/export breadth and PC editing are high-value desktop workflows. | S34 |
| Cisdem ContactsMate | Commercial desktop | active guide | updated 2025 | vendor | Account-level duplicate scans, conflict groups, "Fix All", and trash-based recovery. | S35 |
| Google/Apple/Outlook built-ins | Platform tools | bundled | current help docs | platform vendors | Merge/fix exists but is opaque, limited across accounts, and often not truly destructive merge. | S05-S10 |

## Decision Framework

Impact:

- 5 = central to trust, data retention, or core dedupe value.
- 4 = frequently requested parity or high workflow leverage.
- 3 = valuable once core workflow exists.
- 2 = niche or mostly developer/operator value.
- 1 = not aligned or low user value.

Effort:

- 1 = small local code/docs change.
- 2 = contained module plus tests.
- 3 = multi-module feature.
- 4 = substantial architecture or UX surface.
- 5 = high complexity, sync/protocol/security/legal risk.

Tiers:

- Now: should land before the next credible public release.
- Next: follows once persistence, parser fidelity, and merge safety are stable.
- Later: useful, but not on the critical path to a trustworthy deduper.
- Under Consideration: possible, but needs research, dependency validation, or demand proof.
- Rejected: contradicts local-first scope, license posture, or trust model.

## Prioritized Roadmap

### Now

1. [x] Build a real local data model and migration layer. *(v0.2.0 - ContactRepository + V1 migration)*
   - Sources: L04, L05, S31, S32, S61.
   - Impact 5, effort 3, risk medium.
   - Dependencies: SQLite schema design, contact/source/import tables, migration tests.
   - Novelty: parity.
   - Justification: persistence is prerequisite for safe import preview, undo, history, and any multi-file workflow.

2. [x] Replace the scaffold vCard reader with a standards-aware import/export core. *(v0.2.0 - VERSION-aware reader for 2.1/3.0/4.0 + writer + tests)*
   - Sources: L05, S01, S03, S10, S21, S22, S56-S58, S69.
   - Impact 5, effort 4, risk high.
   - Dependencies: parser choice, license review, golden corpus.
   - Novelty: parity.
   - Justification: data loss in contact cleanup is unacceptable, and current parsing misses many common vCard edge cases.

3. [x] Add import preview, dry-run report, UID/REV idempotence, and rollback snapshots. *(v0.2.0 - ImportPreviewer + ImportPreviewDialog + RollbackService + RestoreHistoryDialog)*
   - Sources: S02, S06, S08, S43-S46.
   - Impact 5, effort 3, risk medium.
   - Dependencies: persistent imports and source records.
   - Novelty: leapfrog versus many built-ins.
   - Justification: users need to know what will be created, updated, skipped, or flagged before importing.

4. [x] Implement source/account attribution everywhere. *(v0.2.0 - SourceId on Contact + every child element + ContactSource table)*
   - Sources: L05, S05, S30, S38, S44, S50.
   - Impact 5, effort 3, risk low.
   - Dependencies: source tables and UI badges.
   - Novelty: parity.
   - Justification: duplicate ambiguity usually comes from multiple sources; every field needs provenance to merge safely.

5. [x] Add libphonenumber-backed normalization and configurable default region. *(v0.2.0 - PhoneNormalizer + AppSettings.DefaultRegion)*
   - Sources: L02, S20, S22, S54, S55.
   - Impact 5, effort 2, risk medium.
   - Dependencies: package review, region setting, tests for shared/home/work numbers.
   - Novelty: parity.
   - Justification: phone formats drive duplicates and false positives; last-7 matching is too blunt.

6. [x] Add email canonicalization profiles. *(v0.2.0 - EmailCanonicalizer with provider profiles)*
   - Sources: L05, S20, S32, S42.
   - Impact 4, effort 2, risk low.
   - Dependencies: matching rule settings and per-provider switches.
   - Novelty: parity.
   - Justification: Gmail/googlemail and plus/dot variants should be explainable and reversible, not hard-coded blindly.

7. [x] Build a transparent weighted match engine with blocking. *(v0.2.0 - DedupEngine two-stage blocking + scoring)*
   - Sources: L05, S20-S22, S51-S53.
   - Impact 5, effort 4, risk medium.
   - Dependencies: normalized fields, persisted contact indexes.
   - Novelty: parity with direct dedupe tools.
   - Justification: exact grouping cannot handle real address books; blocking keeps large books responsive.

8. [x] Add match explanations and threshold profiles. *(v0.2.0 - MatchSignal + Default/Strict/Loose profiles)*
   - Sources: L01, S20, S21, S22, S32, S51.
   - Impact 5, effort 3, risk low.
   - Dependencies: weighted scorer.
   - Novelty: leapfrog versus opaque platform tools.
   - Justification: the project's stated differentiator is transparent fuzzy matching.

9. [x] Build side-by-side merge review with field-level cherry-pick. *(v0.2.0 - MergeEngine + MergeReviewDialog with radio cherry-pick + list union)*
   - Sources: L01, L02, S20, S22, S32, S35, S41.
   - Impact 5, effort 5, risk medium.
   - Dependencies: source attribution, match explanations, undo journal.
   - Novelty: parity.
   - Justification: a deduper without inspectable merge choices is not trustworthy.

10. [x] Complete the undo journal and expose restore history. *(v0.2.0 - undo_journal forward/inverse JSON, MarkUndone, RestoreHistoryDialog, UndoLast for merges)*
    - Sources: L05, S20, S22, S31, S32, S35.
    - Impact 5, effort 4, risk high.
    - Dependencies: persistent contacts and merge operations represented as commands.
    - Novelty: leapfrog versus many built-ins.
    - Justification: users will not trust bulk merges unless every destructive operation is reversible.

11. [x] Add a focused test suite before growing formats. *(v0.2.0 - 41 xunit tests across parser, writer, normalizers, dedup, storage)*
    - Sources: L05, S21, S22, S56-S58.
    - Impact 5, effort 3, risk low.
    - Dependencies: test project and sample corpus.
    - Novelty: parity.
    - Justification: parser and merge bugs destroy data; tests must arrive before new importers.

12. [x] Add accessibility and destructive-action trust basics. *(v0.2.0 - keyboard shortcuts, AutomationProperties.Name on every control, polite live region for status, confirmation dialogs gated by AppSettings.ConfirmDestructiveActions)*
    - Sources: S05, S20, S35, S63.
    - Impact 4, effort 3, risk low.
    - Dependencies: WPF UI pass.
    - Novelty: parity.
    - Justification: keyboard review, clear focus, high contrast, confirmations, and busy states are part of merge safety.

### Next

1. [x] Google/Outlook CSV importers with saved mapping profiles. *(v0.3.0 - GoogleCsvImporter + OutlookCsvImporter wired into preview/snapshot pipeline)*
   - Sources: L01, L02, S08, S09, S34, S42.
   - Impact 4, effort 3, risk medium.
   - Dependencies: import preview and source attribution.
   - Novelty: parity.
   - Justification: CSV is the most common migration path after vCard.

2. [x] Export vCard 3.0/4.0, Google CSV, and Outlook CSV with round-trip tests. *(v0.3.0 - VCardWriter+GoogleCsvWriter+OutlookCsvWriter; multi-format Save dialog; round-trip xunit coverage)*
   - Sources: S01, S03, S08-S10, S21, S22, S34, S56.
   - Impact 5, effort 4, risk high.
   - Dependencies: standards-aware model and test corpus.
   - Novelty: parity.
   - Justification: users need a clean output artifact they can import elsewhere without lock-in.

3. [x] Batch cleanup: intra-contact dedupe, normalize names, dedupe phones/emails/URLs, and bulk regex edit. *(v0.3.0 - BatchCleanup service + CleanupDialog with snapshot capture)*
   - Sources: L02, S20-S22, S35, S46.
   - Impact 4, effort 3, risk medium.
   - Dependencies: undo journal and preview.
   - Novelty: parity.
   - Justification: many duplicates are duplicated fields inside a single contact, not separate cards.

4. [x] Source-aware auto-merge for equivalent/subset records only. *(v0.3.0 - AutoMergeService picks richest record as primary; only merges when secondary is a strict info-subset and confidence ≥ AutoMergeThreshold)*
   - Sources: S20, S22, S32, S35.
   - Impact 4, effort 4, risk high.
   - Dependencies: side-by-side merge, undo, confidence calibration.
   - Novelty: parity.
   - Justification: automation should start where the losing card has no unique information.

5. [x] Photos: parse, preserve, normalize, strip metadata, and perceptual-hash duplicate photos. *(v0.3.0 - PhotoSanitizer strips JPEG APP1..APP15 + PNG ancillary chunks via raw byte walker, no image-decoder dep; 4MB safety cap; integrated into BatchCleanup. Perceptual hashing deferred until ImageSharp/CoenM.ImageHash review.)*
   - Sources: L02, S18-S20, S22, S30, S59, S60.
   - Impact 4, effort 4, risk high.
   - Dependencies: image dependency security/license review and memory limits.
   - Novelty: parity for contacts, uncommon in OSS.
   - Justification: photos are common in mobile exports and are a useful duplicate signal, but image parsing raises security risk.

6. [x] CardDAV read-only client with discovery, ETags, and conflict-safe local import. *(v0.3.0 - CardDavClient with PROPFIND-based discovery (well-known + current-user-principal + addressbook-home-set), ETag-aware enumeration, GET-per-card fetch; CardDavImporter wraps it as IContactImporter; CardDavConnectDialog UI; integrates with the same preview/snapshot/commit/UID-REV-idempotence flow as a local file import)*
   - Sources: L02, S02, S24-S30, S43, S50, S65, S66.
   - Impact 4, effort 5, risk high.
   - Dependencies: import preview, credential storage, source attribution.
   - Novelty: parity.
   - Justification: iCloud, Google, Nextcloud, Baikal, and Radicale are major contact sources, but write sync should wait.

7. [x] Credential storage using Windows Credential Manager/DPAPI. *(v0.3.0 - CredentialVault uses System.Security.Cryptography.ProtectedData with CurrentUser scope; encrypted JSON store; tests verify round-trip and at-rest non-plaintext)*
   - Sources: S28, S29, S50.
   - Impact 4, effort 3, risk high.
   - Dependencies: CardDAV client.
   - Novelty: parity.
   - Justification: local-first does not mean plaintext passwords in config.

8. [x] Search, saved filters, and review queues. *(v0.3.0 - SearchText filter across DisplayName/Org/email/phone/notes + ReviewQueue selector for All / In a duplicate group / Stub / Empty / High confidence)*
   - Sources: L02, S17, S18, S20, S22, S35.
   - Impact 4, effort 3, risk low.
   - Dependencies: persistent indexes.
   - Novelty: parity.
   - Justification: users need to review "possible duplicate", "stub", "empty", and "high-confidence" queues separately.

9. [~] Distribution hardening: installer, portable zip, Authenticode signing, SBOM, checksums, and upgrade notes. *(v0.3.0 - portable zip + SBOM (`dotnet list package` manifest) + SHA256SUMS + warnings-as-errors release pipeline. Authenticode signing and a real installer (.msi/.msix) are still pending.)*
   - Sources: L06, S29, S63, S61, S62.
   - Impact 4, effort 4, risk medium.
   - Dependencies: release workflow redesign.
   - Novelty: parity.
   - Justification: a local desktop data tool must be easy to verify and install safely.

10. [x] Project hygiene: `global.json`, `CONTRIBUTING.md`, issue templates, `SECURITY.md`, dependency scanning, and CI tests. *(v0.3.0 - all six landed; CI workflow runs `dotnet list package --vulnerable --include-transitive` on every push/PR)*
    - Sources: L06, S11, S21, S22, S61-S64.
    - Impact 4, effort 2, risk low.
    - Dependencies: test project.
    - Novelty: parity.
    - Justification: roadmap breadth is pointless without maintainable delivery mechanics.

### Later

1. Outlook PST/OST and MSG import.
   - Sources: L01, L02, S09, S34, S36.
   - Impact 4, effort 5, risk high.
   - Dependencies: license review, safe parser sandbox, import preview.
   - Novelty: leapfrog for OSS.
   - Justification: Outlook import is strategically valuable, but parser and licensing risks are too high for the first stabilization slice.

2. [x] Thunderbird/CardBook/LDIF/MAB migration helpers. *(v0.3.0 - LdifImporter handles RFC 2849 v1 LDIF + Mozilla MAB attribute mapping)*
   - Sources: L01, L02, S10, S18-S20, S41.
   - Impact 3, effort 4, risk medium.
   - Dependencies: robust vCard and CSV path.
   - Novelty: parity.
   - Justification: Thunderbird users are a natural privacy-first segment, but CardDAV/vCard work covers much of this first.

3. Address normalization with offline rules and optional USPS opt-in.
   - Sources: L02, S20, S22, S51-S53.
   - Impact 3, effort 4, risk high.
   - Dependencies: undo and preview.
   - Novelty: parity.
   - Justification: useful for data quality, but external validation must stay explicit and opt-in.

4. LAN CardDAV server/export mode.
   - Sources: L02, S02, S25, S26, S29, S65.
   - Impact 4, effort 5, risk high.
   - Dependencies: stable model, auth, TLS guidance, conflict handling.
   - Novelty: leapfrog.
   - Justification: publishing cleaned contacts over LAN matches the local-first moat, but it is a server product surface.

5. Plugin SDK for importers/exporters.
   - Sources: L02, S25, S28, S65.
   - Impact 3, effort 5, risk high.
   - Dependencies: stable core interfaces and sandbox policy.
   - Novelty: leapfrog.
   - Justification: valuable after built-in formats stabilize; premature plugins would fossilize unstable contracts.

6. [x] CLI/headless cleanup pipeline. *(v0.3.0 - `oc` CLI binary at src/OrganizeContacts.Cli with convert/dedupe/cleanup commands across vCard/CSV/LDIF/jCard)*
   - Sources: S21-S24, S28.
   - Impact 3, effort 4, risk medium.
   - Dependencies: stable core services.
   - Novelty: parity.
   - Justification: power users and support workflows benefit from scripts, but the desktop merge UI is the primary product.

7. [x] jCard and JSContact import/export. *(v0.3.0 - JCardImporter/JCardWriter for RFC 7095. JSContact (RFC 9553) is a closely-related newer format and is on the v0.4 backlog.)*
   - Sources: S03, S04, S67.
   - Impact 3, effort 4, risk medium.
   - Dependencies: vCard 4.0 model.
   - Novelty: forward-looking.
   - Justification: new JSON contact standards matter, but current user data is still mostly vCard/CSV/CardDAV.

8. Cross-platform Avalonia shell.
   - Sources: L01, L02, S23, S24, S27.
   - Impact 3, effort 5, risk high.
   - Dependencies: UI-free core and stable desktop workflows.
   - Novelty: parity.
   - Justification: plausible once Windows product-market fit is proven.

9. Localization framework.
   - Sources: L02, S17, S27, S30, S62.
   - Impact 3, effort 4, risk medium.
   - Dependencies: UI string resource pass.
   - Novelty: parity.
   - Justification: contact data is global, but parser correctness and accessibility precede translated UI.

10. Mobile read-only companion over LAN.
    - Sources: L02, S30, S50.
    - Impact 2, effort 5, risk high.
    - Dependencies: LAN server, auth, mobile stack decision.
    - Novelty: leapfrog.
    - Justification: attractive but far outside the current WPF desktop scope.

### Under Consideration

1. Active-learning duplicate review.
   - Sources: S51-S53.
   - Impact 3, effort 5, risk medium.
   - Dependencies: enough reviewed match/non-match labels.
   - Novelty: leapfrog.
   - Justification: promising for large books, but may be overkill before transparent deterministic scoring is excellent.

2. Business-card scanning and OCR.
   - Sources: S33, S37.
   - Impact 2, effort 5, risk high.
   - Dependencies: offline OCR strategy and privacy review.
   - Novelty: parity with commercial lead-capture tools.
   - Justification: not core to cleaning existing address books.

3. Multi-user collaboration and shared books.
   - Sources: S11, S14, S25-S30, S50.
   - Impact 3, effort 5, risk high.
   - Dependencies: server/sync architecture.
   - Novelty: parity.
   - Justification: conflicts with single-user desktop simplicity unless LAN server mode succeeds.

4. WebDAV Push / instant sync.
   - Sources: S30, S50.
   - Impact 2, effort 5, risk high.
   - Dependencies: CardDAV sync and server support.
   - Novelty: forward-looking.
   - Justification: valuable only after basic CardDAV import/export is reliable.

5. Social handle enrichment from public sources.
   - Sources: S31-S33.
   - Impact 2, effort 5, risk high.
   - Dependencies: explicit opt-in, privacy policy, provider terms review.
   - Novelty: commercial parity.
   - Justification: contradicts the offline differentiator unless handled as user-provided local fields only.

## Rejected

| Idea | Sources | Reason |
|---|---|---|
| Default cloud upload for matching or enrichment | L01, S31-S33 | Contradicts the core local-first promise. |
| Silent telemetry or contact analytics | L01, S27 | Contradicts the no-telemetry trust model. |
| Irreversible bulk merge/delete | S20, S22, S32, S35 | Directly conflicts with reversible cleanup and user trust. |
| Using AGPL/GPL code inside the MIT core without isolation | S25-S27 | License mismatch; can be studied but not embedded casually. |
| Commercial PST SDK by default without a fallback path | L02, S36 | Licensing and redistribution risk; evaluate only behind an abstraction. |
| Social scraping of LinkedIn/GitHub by default | L02, S31-S33 | Privacy, terms, and scope risk; keep any enrichment local and opt-in. |
| Full CRM relationship management before v1 | S27 | Valuable in Monica, but it dilutes the dedupe/import mission. |
| Mobile write-sync before desktop merge safety | S30, S50 | Multiplies conflict risk before local model and undo are trustworthy. |
| AI-generated contact updates from the web | S31-S33 | Cloud dependence and hallucination risk are misaligned with reliable cleanup. |
| Auto-deleting low-information contacts without preview | S20, S22, S35 | Even "empty" or "stub" contacts can contain user-meaningful context. |

## Raw Feature Harvest and Prioritization

Abbreviations: I = impact, E = effort, R = risk, N = novelty. Fit is "Y", "N", or "Maybe".

| ID | Feature | Category | Prevalence | Sources | Fit | I | E | R | Depends on | N | Tier | Placement reason |
|---|---|---|---|---|---:|---:|---:|---|---|---|---|---|
| F01 | SQLite contact/source/import persistence | data, reliability | table stakes | L04, L05, S31, S32, S61 | Y | 5 | 3 | Med | schema | Parity | Now | Required for every durable workflow. |
| F02 | vCard 2.1/3.0/4.0 parser and writer | data, migration | table stakes | S01, S21, S22, S56-S58 | Y | 5 | 4 | High | tests | Parity | Now | Format breadth starts with correct vCard. |
| F03 | vCard line folding, quoted-printable, charsets, grouped props | data, reliability | table stakes | S01, S03, S21, S22, S45, S69 | Y | 5 | 4 | High | parser | Parity | Now | Current parser handles only a subset. |
| F04 | UID/REV import idempotence | reliability, migration | common pain | S01, S02, S44, S46 | Y | 5 | 3 | Med | persistence | Leapfrog | Now | Prevents duplicate creation on repeated imports. |
| F05 | Import preview/dry run | UX, reliability | common | S06, S09, S35, S46 | Y | 5 | 3 | Med | persistence | Leapfrog | Now | Makes risky imports inspectable. |
| F06 | Rollback snapshot before import | reliability | common premium | S31, S32, S35 | Y | 5 | 3 | Med | persistence | Parity | Now | Required for user trust. |
| F07 | Source/account attribution per field | data, UX | table stakes | S05, S30, S38, S44 | Y | 5 | 3 | Low | schema | Parity | Now | Explains where duplicates came from. |
| F08 | Contact schema expansion for vCard 4 | data | common | S01, S04, S18, S56 | Y | 4 | 3 | Med | parser | Parity | Now | Avoids dropping fields during cleanup. |
| F09 | X-* custom field preservation | data, migration | common | S18, S19, S67 | Y | 4 | 3 | Med | parser | Parity | Now | Prevents vendor data loss. |
| F10 | libphonenumber E.164 normalization | data, dedup | table stakes | S20, S22, S54, S55 | Y | 5 | 2 | Med | settings | Parity | Now | Phone formats are a central match signal. |
| F11 | Region-specific phone settings | UX, i18n | common | S20, S22, S54, S55 | Y | 4 | 2 | Med | F10 | Parity | Now | E.164 cannot be guessed safely. |
| F12 | Phone kind inference | data | rare | S20, S22 | Y | 3 | 3 | Med | F10 | Leapfrog | Next | Useful after base normalization. |
| F13 | Email canonicalization profiles | data, dedup | common | S20, S32, S42 | Y | 4 | 2 | Low | settings | Parity | Now | Needs transparent provider-specific rules. |
| F14 | Name normalization with prefixes, initials, diacritics | data, dedup | common | S20, S21, S22 | Y | 4 | 3 | Med | scorer | Parity | Now | Reduces false negatives. |
| F15 | Weighted fuzzy scoring | dedup | table stakes | S20-S22, S51-S53 | Y | 5 | 4 | Med | normalization | Parity | Now | Exact grouping is insufficient. |
| F16 | Blocking/indexing for large books | performance | common in ER | S20, S22, S51-S53 | Y | 4 | 4 | Med | persistence | Parity | Now | Avoids O(n^2) pain. |
| F17 | Match explanation strings | UX, trust | table stakes | L01, S20, S22, S32 | Y | 5 | 3 | Low | scorer | Leapfrog | Now | Core differentiator in README. |
| F18 | Threshold profiles and sliders | UX | common | L02, S21, S22, S32 | Y | 4 | 3 | Med | scorer | Parity | Now | Makes fuzzy matching user-tunable. |
| F19 | False-positive guardrails | reliability | table stakes | S20, S38, S40, S41 | Y | 5 | 3 | Med | scorer | Parity | Now | Same name can mean different people. |
| F20 | Side-by-side merge review | UX | table stakes | L01, S20, S22, S32, S35 | Y | 5 | 5 | Med | source attrs | Parity | Now | Main user workflow. |
| F21 | Field-level cherry-pick | UX, data | common premium | L01, S20, S22, S35 | Y | 5 | 5 | Med | F20 | Parity | Now | Prevents lossy merges. |
| F22 | Survivorship policy editor | UX, data | common | S20, S22, S35 | Y | 4 | 4 | Med | F20 | Parity | Next | Auto rules need visual review first. |
| F23 | Equivalent/subset auto-merge | UX, reliability | common | S20, S22, S32 | Y | 4 | 4 | High | undo | Parity | Next | Safe only after restore path exists. |
| F24 | Full undo journal | reliability | common premium | L05, S20, S22, S31, S32 | Y | 5 | 4 | High | commands | Leapfrog | Now | Non-negotiable for destructive operations. |
| F25 | Audit/history viewer | observability | common | S22, S31, S32 | Y | 4 | 3 | Low | journal | Parity | Next | Lets users inspect and restore work. |
| F26 | Intra-contact field dedupe | data | common | S20, S22, S44, S46 | Y | 4 | 3 | Med | journal | Parity | Next | Solves duplicated phones/emails inside a card. |
| F27 | Batch normalize names/phones/emails | UX, data | common | L02, S21, S22 | Y | 4 | 3 | Med | undo | Parity | Next | High leverage after preview/undo. |
| F28 | Bulk regex find/replace | UX | rare power-user | L02, S21 | Y | 3 | 3 | Med | undo | Parity | Next | Useful but risky without preview. |
| F29 | Stub/spam/empty contact classification | UX, data | rare | S22, S32 | Y | 3 | 3 | Med | scorer | Leapfrog | Next | Helps triage messy imports. |
| F30 | Saved filters/review queues | UX | common | S17, S18, S22, S35 | Y | 4 | 3 | Low | indexes | Parity | Next | Keeps large cleanup sessions manageable. |
| F31 | Google CSV importer | migration | table stakes | L01, S08, S42 | Y | 4 | 3 | Med | preview | Parity | Next | Major export format. |
| F32 | Outlook CSV importer | migration | table stakes | L01, S09, S34 | Y | 4 | 3 | Med | preview | Parity | Next | Major Windows workflow. |
| F33 | Custom CSV mapping | migration, UX | common | S09, S34, S42 | Y | 4 | 4 | Med | CSV core | Parity | Next | Handles vendor variants. |
| F34 | vCard/CSV export | migration | table stakes | S01, S08-S10, S21, S22, S34 | Y | 5 | 4 | High | parser/model | Parity | Next | Avoids lock-in. |
| F35 | Export diff/report | docs, observability | rare | S22, S35 | Y | 3 | 2 | Low | export | Leapfrog | Next | Shows what changed. |
| F36 | Contact photo parsing | data | common | S18-S20, S22, S30 | Y | 4 | 3 | High | image safety | Parity | Next | Mobile exports commonly include photos. |
| F37 | Photo perceptual-hash dedupe | dedup, data | rare | L02, S20, S59 | Y | 4 | 4 | High | F36 | Leapfrog | Next | Useful signal but dependency-sensitive. |
| F38 | Photo EXIF stripping/resizing | security, data | common | L02, S22, S60 | Y | 4 | 3 | High | image safety | Parity | Next | Prevents metadata leakage and bloat. |
| F39 | Image parser sandbox/limits | security | table stakes | S60 | Y | 4 | 3 | High | F36 | Parity | Next | Image CVEs make limits mandatory. |
| F40 | CardDAV read-only import | integration | common | S02, S24-S30, S43, S50 | Y | 4 | 5 | High | persistence | Parity | Next | Major source class, safer read-only first. |
| F41 | CardDAV discovery and ETag handling | integration, reliability | table stakes | S02, S24, S28-S30, S43 | Y | 4 | 5 | High | F40 | Parity | Next | Required for correct sync semantics. |
| F42 | CardDAV write-back sync | integration | common | S02, S24-S30 | Maybe | 4 | 5 | High | F40, conflicts | Parity | Later | Too risky before local merge safety. |
| F43 | Credential vault | security | table stakes | S28, S29, S50 | Y | 4 | 3 | High | F40 | Parity | Next | Needed before any authenticated source. |
| F44 | Outlook PST/OST import | migration | common commercial | L01, S09, S34, S36 | Y | 4 | 5 | High | parser review | Leapfrog | Later | High value, high dependency/license risk. |
| F45 | Outlook MSG contact import | migration | rare | L02, S36 | Y | 3 | 4 | High | F44 | Parity | Later | Useful after PST path. |
| F46 | Thunderbird/CardBook import helpers | migration | common niche | S10, S18-S20, S41 | Y | 3 | 4 | Med | vCard export | Parity | Later | Good privacy segment, not first source. |
| F47 | Android `.vcf` photo round-trip | migration | common | L02, S30, S50 | Y | 4 | 4 | Med | parser/photos | Parity | Next | Important for phone migrations. |
| F48 | Android `contacts2.db` import | migration | rare | S58 | Maybe | 2 | 4 | Med | parser/model | Leapfrog | Later | Niche but possible via VisualCard pattern. |
| F49 | Categories/groups/labels | data, UX | table stakes | S13, S18, S19, S30, S67 | Y | 4 | 3 | Med | schema | Parity | Next | Contact organization must survive cleanup. |
| F50 | Nested categories | UX | emerging | S13 | Y | 3 | 3 | Med | F49 | Parity | Later | Useful but not core dedupe. |
| F51 | Shared/read-only address book awareness | integration | common | S14, S25-S30, S50 | Y | 3 | 4 | High | CardDAV | Parity | Later | Needed once sync exists. |
| F52 | Address normalization | data | common | L02, S20, S22 | Y | 3 | 4 | High | undo | Parity | Later | Risky without region-specific rules. |
| F53 | Optional USPS validation | integration | rare | L02 | Maybe | 2 | 4 | High | privacy opt-in | Parity | Later | External call must stay opt-in. |
| F54 | Birthday/anniversary cleanup | data | common | L02, S11, S27 | Y | 3 | 3 | Low | schema | Parity | Later | Useful after date fields are preserved. |
| F55 | Calendar export for birthdays | integration | common | S11, S27 | Maybe | 2 | 3 | Low | F54 | Parity | Later | Adjacent, not core. |
| F56 | Relationship/PRM fields | data | common CRM | S27 | Maybe | 2 | 4 | Med | schema | Parity | Under Consideration | Risks CRM scope creep. |
| F57 | QR code/contact share | UX | common | S48 | Maybe | 2 | 3 | Low | export | Parity | Later | Handy but secondary. |
| F58 | Business-card OCR | mobile, integration | common commercial | S33, S37 | Maybe | 2 | 5 | High | offline OCR | Parity | Under Consideration | Not core cleanup. |
| F59 | Public web/social enrichment | data | commercial | S31-S33 | N | 2 | 5 | High | privacy review | Parity | Rejected | Conflicts with offline value. |
| F60 | Local social handle fields | data | common | S27, S31-S33 | Y | 2 | 2 | Low | schema | Parity | Later | Fine if user-provided. |
| F61 | LAN CardDAV server | platform | rare OSS desktop | L02, S02, S25, S26, S65 | Y | 4 | 5 | High | stable model | Leapfrog | Later | Strong local-first extension, but server scope. |
| F62 | WebDAV Push | platform | emerging | S30, S50 | Maybe | 2 | 5 | High | sync | Leapfrog | Under Consideration | Premature before CardDAV basics. |
| F63 | Plugin SDK | plugin ecosystem | common mature apps | L02, S25, S28, S65 | Y | 3 | 5 | High | stable APIs | Leapfrog | Later | Wait until core contracts settle. |
| F64 | CLI/headless mode | dev-experience | common OSS | S21-S24, S28 | Y | 3 | 4 | Med | core services | Parity | Later | Useful after UI workflow is stable. |
| F65 | JSON output/API | dev-experience | common CLI | S28 | Y | 2 | 3 | Low | CLI | Parity | Later | Complements headless mode. |
| F66 | jCard support | data, standards | rare | S03 | Y | 3 | 3 | Med | vCard model | Forward | Later | Future-proof but not demanded yet. |
| F67 | JSContact support | data, standards | emerging | S04, S67 | Maybe | 3 | 4 | Med | vCard model | Forward | Later | Monitor adoption. |
| F68 | `global.json` SDK pin | dev-experience | table stakes | L04, S63 | Y | 3 | 1 | Low | none | Parity | Next | Reduces SDK drift. |
| F69 | Upgrade packages | security | table stakes | L04, S61-S64 | Y | 4 | 2 | Med | build/test | Parity | Next | Keeps SQLite/.NET servicing current. |
| F70 | Dependency scanning and SBOM | security | table stakes | S60-S64 | Y | 4 | 2 | Low | CI | Parity | Next | Needed for desktop data app trust. |
| F71 | Parser fuzz tests | testing, security | common for parsers | S01, S21, S22, S56-S58 | Y | 5 | 3 | Med | test project | Parity | Now | Parser failures can corrupt data. |
| F72 | Golden round-trip corpus | testing | table stakes | S21, S22, S56-S58 | Y | 5 | 3 | Low | parser | Parity | Now | Verifies no field loss. |
| F73 | Performance benchmarks | performance | common | S51-S53 | Y | 3 | 3 | Low | scorer | Parity | Later | Useful after algorithm stabilizes. |
| F74 | UI virtualization for large books | performance | common desktop | S17, S20, S35 | Y | 3 | 3 | Low | persistence | Parity | Later | Needed when large imports are common. |
| F75 | Accessibility keyboard/screen reader | accessibility | table stakes | S05, S20, S35 | Y | 4 | 3 | Low | UI | Parity | Now | Merge review must work without mouse. |
| F76 | High contrast/light theme | accessibility, UX | common | S17, S62 | Y | 3 | 3 | Low | theme tokens | Parity | Next | Current app is dark-only. *(v0.3.0 - Catppuccin Latte theme + Settings picker; live theme switch.)* |
| F77 | Localization framework | i18n | common | L02, S17, S27, S30 | Y | 3 | 4 | Med | UI resources | Parity | Later | Data is global; UI can follow core. |
| F78 | Docs: migration recipes | docs | table stakes | S21-S24, S29, S34 | Y | 4 | 2 | Low | features | Parity | Next | Users need source-specific guidance. |
| F79 | In-app rule explanations/help | docs, UX | common | L01, S20, S22 | Y | 4 | 2 | Low | scorer | Leapfrog | Now | Transparency must be visible in product. |
| F80 | `CONTRIBUTING.md`, issue templates, `SECURITY.md` | docs, dev-experience | table stakes | S11, S21, S22 | Y | 3 | 2 | Low | none | Parity | Next | Needed for OSS maturity. |
| F81 | Installer and portable zip | distribution | table stakes | L06, S29, S34 | Y | 4 | 3 | Med | release workflow | Parity | Next | Zip alone is not enough for mainstream Windows. |
| F82 | Authenticode signing | distribution, security | common | L06, S29 | Y | 4 | 4 | Med | cert | Parity | Next | Reduces install friction. |
| F83 | Auto-update channel | distribution | common | L02, S29, S63 | Y | 3 | 4 | High | signing | Parity | Later | Needs trust model and rollback. |
| F84 | Crash logs/local diagnostics | observability | common | S29, S32 | Y | 3 | 3 | Low | logging | Parity | Next | Local logs are compatible with no telemetry. |
| F85 | Optional telemetry | telemetry | common SaaS | S31-S33 | N | 1 | 3 | High | privacy policy | Parity | Rejected | Violates no-telemetry promise unless user later requests opt-in. |
| F86 | Multi-user collaboration | multi-user | common groupware | S11, S25-S30 | Maybe | 3 | 5 | High | server/sync | Parity | Under Consideration | Not aligned with single-user desktop v1. |
| F87 | Cross-platform Avalonia shell | platform | common aspiration | L02, S23, S24 | Y | 3 | 5 | High | UI-free core | Parity | Later | Reasonable after Windows core proves itself. |
| F88 | Mobile companion | mobile | rare | L02, S30 | Maybe | 2 | 5 | High | LAN server | Leapfrog | Later | Too broad before desktop core. |
| F89 | Active-learning scorer | dedup | advanced | S51-S53 | Maybe | 3 | 5 | Med | labels | Leapfrog | Under Consideration | Needs user-labeled data and clear UX. |
| F90 | Full CRM features | UX, data | common CRM | S27 | N | 2 | 5 | Med | many | Parity | Rejected | Dilutes dedupe/import mission. |

## Delivery Sequence

### Release 0.2 - Trustworthy local data and vCard

- Persistent contacts/sources/imports with migrations.
- Parser/writer decision and implementation for vCard 2.1/3.0/4.0.
- Golden corpus and parser fuzz/property tests.
- Import preview, dry-run report, UID/REV idempotence, rollback snapshot.
- Source/account attribution in data model and UI.
- libphonenumber normalization, email canonicalization, and name normalization.

### Release 0.3 - Transparent duplicate review

- Blocking and weighted duplicate scoring.
- Match explanations and threshold profiles.
- Side-by-side merge UI with field cherry-pick.
- Undo journal, audit viewer, and restore.
- Accessibility pass for keyboard, focus, screen reader names, high contrast basics.

### Release 0.4 - Batch cleanup and export

- Intra-contact field dedupe and sanitize commands.
- Batch normalize, regex edit, saved filters, and review queues.
- Google/Outlook CSV importers and custom mapping.
- vCard/CSV export with round-trip tests and export report.
- Local diagnostics, source-specific migration docs, and OSS contribution docs.

### Release 0.5 - Photos and sync sources

- Photo parse/preserve/export, EXIF stripping, size limits, and optional perceptual hash matching.
- CardDAV read-only import with discovery, ETags, credential vault, and conflict-safe local snapshots.
- Android `.vcf` photo round-trip and Thunderbird/CardBook migration helpers if core vCard coverage is stable.

### Release 1.0 - Hardened Windows release

- Installer plus portable zip.
- Authenticode signing, SBOM, dependency scan, checksums, release notes, and upgrade guide.
- Performance benchmarks for large address books.
- Stable plugin-facing importer/exporter abstractions, but no public plugin SDK until contracts are proven.

## Category Coverage Audit

- Security: credential vault, image parser limits, dependency scanning, SBOM, signed releases, security policy.
- Accessibility: keyboard merge flow, screen reader names, high contrast, destructive action confirmations.
- i18n/l10n: region-aware phone parsing now; UI localization later.
- Observability/telemetry: local audit/history/diagnostics only; telemetry rejected.
- Testing: parser corpus, fuzz tests, unit/integration tests, benchmarks.
- Docs: migration recipes, rule explanations, contributing/security docs.
- Distribution/packaging: installer, portable zip, signing, checksums, SBOM, upgrade notes.
- Plugin ecosystem: later, after core interfaces stabilize.
- Mobile: Android `.vcf` round-trip next; mobile companion later.
- Offline/resilience: local-first persistence, snapshots, undo, no cloud matching.
- Multi-user/collab: under consideration only after LAN/CardDAV server work.
- Migration paths: vCard, CSV, CardDAV, Outlook, Thunderbird, Android.
- Upgrade strategy: migrations, `global.json`, package update policy, release workflow hardening.

## Self-Audit

- Every roadmap item references local evidence or source IDs listed below.
- Rejected items are explicit and do not reappear in accepted tiers.
- The Now tier is limited to prerequisites for trustworthy local import, matching, merge, undo, and verification.
- High-risk dependency areas are isolated: PST/OST, image processing, CardDAV write sync, cloud enrichment, and plugins.
- The roadmap preserves the project philosophy: offline by default, format breadth, transparent fuzzy matching, reversible merge.
- `ROADMAP.md` is located at the repository root.

## Appendix A - Local Evidence

| ID | Evidence |
|---|---|
| L01 | `README.md` - project philosophy, claimed differentiators, current feature list. |
| L02 | previous `ROADMAP.md` - original milestone sketch. |
| L03 | `CHANGELOG.md` - v0.1.0 shipped scaffold summary. |
| L04 | `src/OrganizeContacts.App/OrganizeContacts.App.csproj` and `src/OrganizeContacts.Core/OrganizeContacts.Core.csproj` - .NET targets and package pins. |
| L05 | `src/OrganizeContacts.Core/**` and `src/OrganizeContacts.App/ViewModels/MainViewModel.cs` - actual parser, dedup, storage, UI behavior. |
| L06 | `.github/workflows/release.yml` - current release packaging. |

## Appendix B - External Sources

| ID | URL |
|---|---|
| S01 | https://www.rfc-editor.org/rfc/rfc6350 |
| S02 | https://www.rfc-editor.org/rfc/rfc6352 |
| S03 | https://www.rfc-editor.org/rfc/rfc7095 |
| S04 | https://www.rfc-editor.org/rfc/rfc9553 |
| S05 | https://support.apple.com/guide/iphone/merge-or-hide-duplicate-contacts-iph2ab28320d/ios |
| S06 | https://support.apple.com/en-au/guide/contacts/adrbk1498/mac |
| S07 | https://support.google.com/contacts/answer/7078226 |
| S08 | https://support.google.com/contacts/answer/7199294 |
| S09 | https://support.microsoft.com/office/import-contacts-to-outlook-for-windows-bb796340-b58a-46c1-90c7-b549b8f3c5f8 |
| S10 | https://developer.thunderbird.net/thunderbird-development/codebase-overview/address-book |
| S11 | https://github.com/nextcloud/contacts |
| S12 | https://github.com/nextcloud/contacts/issues/5246 |
| S13 | https://github.com/nextcloud/contacts/issues/5192 |
| S14 | https://github.com/nextcloud/contacts/issues/5191 |
| S15 | https://github.com/nextcloud/contacts/issues/5245 |
| S16 | https://github.com/nextcloud/contacts/issues/5277 |
| S17 | https://github.com/FossifyOrg/Contacts |
| S18 | https://services.addons.thunderbird.net/EN-us/thunderbird/addon/cardbook/ |
| S19 | https://gitlab.com/CardBook |
| S20 | https://github.com/DDvO/Duplicate-Contacts-Manager |
| S21 | https://github.com/mbideau/vcardtools |
| S22 | https://github.com/cedi-ch/kontakt-schnabel |
| S23 | https://github.com/lucc/khard |
| S24 | https://github.com/pimutils/vdirsyncer |
| S25 | https://github.com/Kozea/Radicale |
| S26 | https://github.com/sabre-io/Baikal |
| S27 | https://github.com/monicahq/monica |
| S28 | https://github.com/pimalaya/cardamum |
| S29 | https://github.com/etesync/etesync-dav |
| S30 | https://www.davx5.com/ |
| S31 | https://support.contactsplus.com/hc/en-us/articles/22538374387611-Contacts-Premium-Trial |
| S32 | https://support.contactsplus.com/hc/en-us/articles/4407278226459-The-Assistant-Applying-Updates-and-Duplicates |
| S33 | https://covve.com/pricing |
| S34 | https://www.copytrans.net/support/user-guides-copytrans-contacts/ |
| S35 | https://www.cisdem.com/resource/cisdem-contactsmate-mac-advanced-guide.html |
| S36 | https://imazing.com/documentation/iMazing-CLI-Documentation.pdf |
| S37 | https://play.google.com/store/apps/details?id=com.forteam.mergix |
| S38 | https://www.reddit.com/r/SimpleMobileTools/comments/ugqikb |
| S39 | https://www.reddit.com/r/iphone/comments/13ftk6b |
| S40 | https://www.reddit.com/r/macapps/comments/1okjfir |
| S41 | https://www.reddit.com/r/Thunderbird/comments/1ahpccw |
| S42 | https://www.reddit.com/r/openphone/comments/1jbajmm |
| S43 | https://stackoverflow.com/questions/50655191/how-do-i-detect-changes-in-vcards |
| S44 | https://apple.stackexchange.com/questions/433906/how-can-i-import-a-vcard-without-getting-duplicated-fields |
| S45 | https://forum.mudita.com/t/contacts-duplicated-missing-labels-after-importing-from-vcard/12466 |
| S46 | https://correctvcf.com/help/fix-duplicate-contacts-vcf-import/ |
| S47 | https://github.com/rwsturm/awesome-selfhosted |
| S48 | https://docs.kde.org/stable_kf6/en/kaddressbook/kaddressbook/ |
| S49 | https://wiki.gnome.org/Apps%282f%29Contacts.html |
| S50 | https://manual.davx5.com/introduction.html |
| S51 | https://github.com/dedupeio/dedupe |
| S52 | https://docs.dedupe.io/ |
| S53 | https://fritshermans.github.io/posts/Deduplipy.html |
| S54 | https://github.com/google/libphonenumber/blob/master/FALSEHOODS.md |
| S55 | https://github.com/twcclegg/libphonenumber-csharp |
| S56 | https://www.nuget.org/packages/FolkerKinzel.VCards |
| S57 | https://github.com/mixerp/MixERP.Net.VCards |
| S58 | https://github.com/Aptivi/VisualCard |
| S59 | https://github.com/coenm/ImageHash |
| S60 | https://github.com/advisories/GHSA-2cmq-823j-5qj8 |
| S61 | https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.7 |
| S62 | https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2 |
| S63 | https://devblogs.microsoft.com/dotnet/dotnet-10-0-7-oob-security-update/ |
| S64 | https://nvd.nist.gov/vuln/detail/CVE-2025-6965 |
| S65 | https://github.com/sabre-io/dav |
| S66 | https://github.com/natelindev/tsdav |
| S67 | https://bugzilla.mozilla.org/show_bug.cgi?id=2013764 |
| S68 | https://github.com/topics/contact-manager |
| S69 | https://www.rfc-editor.org/rfc/rfc6868 |
| S70 | https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100 |
