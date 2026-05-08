# Migration recipes

Step-by-step instructions for getting your contacts out of the major sources
and into OrganizeContacts. Every path here is local-first and reversible — the
preview dialog plus rollback snapshots mean nothing destructive happens until
you confirm.

## Google Contacts

1. Visit <https://contacts.google.com> in a desktop browser.
2. Top-left → **Export** → choose **Google CSV** (preferred) or
   **vCard (for iOS Contacts)**.
3. In OrganizeContacts: header → **Import Google CSV…** (or
   **Import vCard…**) and pick the file.
4. The preview dialog lists every card it found. Inspect the action column:
   - `New` will be created.
   - `UpdateNewer` matches an existing UID and the incoming `REV` is newer.
   - `SkipUnchanged` / `SkipOlder` are safe — nothing will be touched.
5. Tick **Capture rollback snapshot before commit** (default) and click
   **Commit import**.
6. After the import, run **Cleanup…** → tick **Normalize phones to E.164** and
   **Canonicalize emails (provider profiles)** so dedupe later works on a
   common shape.

## iCloud Contacts

1. Sign in to <https://www.icloud.com/contacts>.
2. Side panel ⚙ → **Select All Contacts** → ⚙ again → **Export vCard**.
3. iCloud emits a `.vcf` (vCard 3.0). Use **Import vCard…**.

## Outlook for Windows

1. File → **Open & Export** → **Import/Export** → **Export to a file** →
   **Comma Separated Values** → pick the **Contacts** folder.
2. Outlook produces a CSV with the English column schema OrganizeContacts
   recognises.
3. Use **Import Outlook CSV…**.

## Outlook on the web (Microsoft 365)

1. <https://outlook.live.com/people> → **Manage contacts** → **Export
   contacts** → choose **All contacts** + **CSV**.
2. The exported CSV is the same Outlook English schema. Use **Import Outlook
   CSV…**.

## Android (any vendor)

1. Open the **Contacts** app on the device.
2. ⋮ menu → **Settings** → **Export to .vcf file** (the exact label varies by
   vendor — Samsung labels it "Move device contacts", Pixel labels it
   "Export").
3. Move the resulting `.vcf` to your PC (USB, share-link, Nextcloud, etc.)
   and use **Import vCard…**.

## Thunderbird / CardBook

1. Right-click the address book → **Export** → choose vCard.
2. CardBook produces a multi-card `.vcf`. OrganizeContacts handles vCard 2.1,
   3.0, and 4.0 in the same file.
3. Use **Import vCard…**.

## CSV from another tool

If your CSV doesn't match Google or Outlook's schema, the importer will not
auto-recognise it. The roadmap item *"Custom CSV mapping"* (Next#3 in the
F-table) is on the path. In the meantime, the easiest workaround is to open
the CSV in a spreadsheet and rename the column headers to match Google's
schema before import.

## After import

- The header status bar reports `+N new, ~M updated, K skipped` — verify it
  matches what you expected.
- **Rescan** to surface duplicates. Each group lists the per-field signals
  that contributed to the match (`exact name +0.50`, `phone E.164 +0.45`).
- **Cleanup…** does intra-contact dedupe and normalization.
- **Auto-merge** runs the conservative subset rule across the whole list.
- Anything destructive can be reversed: **Undo** for the last merge,
  **History…** for the full list of imports and rollback snapshots.
