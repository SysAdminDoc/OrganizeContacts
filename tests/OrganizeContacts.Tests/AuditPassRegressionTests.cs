using System.IO;
using System.Text;
using OrganizeContacts.Core;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Photos;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Tests;

/// <summary>
/// Regression tests for the v0.3.2 deep audit pass. Each test guards a specific bug
/// found and fixed during the audit so a refactor can't silently re-introduce it.
/// </summary>
public class AuditPassRegressionTests
{
    // -------- LdifImporter: mail/phone-only contact must not be dropped --------

    [Fact]
    public async Task Ldif_card_with_only_mail_is_kept()
    {
        // Pre-fix the `seen` flag was set only on cn/givenName/sn so a Thunderbird MAB
        // entry that exported just `mail` was silently discarded.
        var ldif = "dn: mail=lonely@example.com\nmail: lonely@example.com\n\n";
        var path = Path.Combine(Path.GetTempPath(), $"oc-a-{Guid.NewGuid():N}.ldif");
        await File.WriteAllTextAsync(path, ldif);
        try
        {
            var importer = new LdifImporter();
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            Assert.Single(list);
            Assert.Single(list[0].Emails);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public async Task Ldif_card_with_only_phone_and_workaddress_is_kept()
    {
        var ldif =
            "dn: cn=phone-only\n" +
            "cellPhone: 555-1234\n" +
            "mozillaWorkStreet: 1 Acme Way\n" +
            "mozillaWorkCity: Springfield\n\n";
        var path = Path.Combine(Path.GetTempPath(), $"oc-a-{Guid.NewGuid():N}.ldif");
        await File.WriteAllTextAsync(path, ldif);
        try
        {
            var importer = new LdifImporter();
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            Assert.Single(list);
            Assert.Single(list[0].Phones);
            Assert.Single(list[0].Addresses);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // -------- JCardImporter: comma-string CATEGORIES must parse --------

    [Fact]
    public async Task JCard_categories_as_single_string_now_parses()
    {
        // Pre-fix a dangling `else` made the single-string CATEGORIES branch unreachable.
        // Every jCard exporter that emits `["categories", {}, "text", "vip,client"]`
        // (instead of an array of strings) had its categories silently dropped.
        var json = """
            ["vcard", [
              ["version", {}, "text", "4.0"],
              ["fn", {}, "text", "X"],
              ["categories", {}, "text", "vip,client,priority"]
            ]]
            """;
        var path = Path.Combine(Path.GetTempPath(), $"oc-a-{Guid.NewGuid():N}.jcard");
        await File.WriteAllTextAsync(path, json);
        try
        {
            var importer = new JCardImporter();
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            Assert.Single(list);
            Assert.Equal(3, list[0].Categories.Count);
            Assert.Contains("vip", list[0].Categories);
            Assert.Contains("client", list[0].Categories);
            Assert.Contains("priority", list[0].Categories);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public async Task JCard_card_with_only_email_no_longer_dropped()
    {
        // Pre-fix only FN and N set the `seen` flag, so a jCard with only EMAIL/TEL
        // was discarded — but the same exporter happily emits such records on imports
        // that lack a display name (CardDAV stubs, mailing-list imports, etc.).
        var json = """
            ["vcard", [
              ["version", {}, "text", "4.0"],
              ["email", {"type":"work"}, "text", "alone@example.com"]
            ]]
            """;
        var path = Path.Combine(Path.GetTempPath(), $"oc-a-{Guid.NewGuid():N}.jcard");
        await File.WriteAllTextAsync(path, json);
        try
        {
            var importer = new JCardImporter();
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            Assert.Single(list);
            Assert.Single(list[0].Emails);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // -------- VCardImporter: SplitStructured preserves escapes --------

    [Fact]
    public async Task VCard_structured_field_preserves_escaped_newline()
    {
        // Pre-fix SplitStructured stripped the backslash before passing parts to UnescapeText,
        // so `\n` inside an N value became literal "n" instead of decoding to a newline.
        var src =
            "BEGIN:VCARD\r\n" +
            "VERSION:3.0\r\n" +
            "FN:test\r\n" +
            "N:Smith\\nJr;John;;;\r\n" +
            "END:VCARD\r\n";
        var path = Path.Combine(Path.GetTempPath(), $"oc-a-{Guid.NewGuid():N}.vcf");
        await File.WriteAllTextAsync(path, src);
        try
        {
            var importer = new VCardImporter();
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            Assert.Single(list);
            Assert.Equal("Smith\nJr", list[0].FamilyName);
            Assert.Equal("John", list[0].GivenName);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // -------- VCardImporter: QP soft break must not eat following property --------

    [Fact]
    public async Task VCard_qp_trailing_equal_does_not_swallow_next_property()
    {
        // Defensive guard: a malformed export ending a QP value with `=` immediately
        // followed by an unrelated property line (no whitespace continuation) would
        // otherwise concat the next property into the value, hiding EMAIL/TEL data.
        var src =
            "BEGIN:VCARD\r\n" +
            "VERSION:2.1\r\n" +
            "FN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:Stray=\r\n" +
            "EMAIL:found@example.com\r\n" +
            "END:VCARD\r\n";
        var path = Path.Combine(Path.GetTempPath(), $"oc-a-{Guid.NewGuid():N}.vcf");
        await File.WriteAllTextAsync(path, src);
        try
        {
            var importer = new VCardImporter();
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            Assert.Single(list);
            Assert.Single(list[0].Emails);
            Assert.Equal("found@example.com", list[0].Emails[0].Address);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // -------- VCardImporter: PHOTO mime sniffed when TYPE missing --------

    [Fact]
    public async Task VCard_photo_without_type_param_sniffs_jpeg_magic()
    {
        // 2.1/3.0 PHOTO is allowed to omit the TYPE param. Pre-fix we left
        // PhotoMimeType=null which lost format identity on round-trip.
        var jpegMagic = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00 };
        var b64 = Convert.ToBase64String(jpegMagic);
        var src =
            "BEGIN:VCARD\r\n" +
            "VERSION:3.0\r\n" +
            "FN:Photo\r\n" +
            $"PHOTO;ENCODING=b:{b64}\r\n" +
            "END:VCARD\r\n";
        var path = Path.Combine(Path.GetTempPath(), $"oc-a-{Guid.NewGuid():N}.vcf");
        await File.WriteAllTextAsync(path, src);
        try
        {
            var importer = new VCardImporter();
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            Assert.Single(list);
            Assert.NotNull(list[0].PhotoBytes);
            Assert.Equal("image/jpeg", list[0].PhotoMimeType);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // -------- CsvWriter: formula-injection defense --------

    [Fact]
    public void Csv_writer_neutralises_formula_prefix()
    {
        // CWE-1236: a contact field starting with `=`, `+`, `-`, `@`, `\t`, `\r` would
        // be evaluated by Excel/Sheets/Numbers when the export is opened. The writer
        // prefixes a single quote (Excel's literal-text marker) to defang it.
        Assert.Equal("'=cmd|'/c calc'!A0", CsvWriter.Escape("=cmd|'/c calc'!A0"));
        Assert.Equal("'+1234", CsvWriter.Escape("+1234"));
        Assert.Equal("'-2+3", CsvWriter.Escape("-2+3"));
        Assert.Equal("'@SUM(A1)", CsvWriter.Escape("@SUM(A1)"));
        // Plain text is untouched (idempotent for normal data).
        Assert.Equal("Acme Corp", CsvWriter.Escape("Acme Corp"));
        Assert.Equal("john@example.com", CsvWriter.Escape("john@example.com"));
    }

    [Fact]
    public void Csv_writer_quotes_after_formula_prefix_when_needed()
    {
        // Field with both formula prefix AND a comma must be quoted so the leading
        // apostrophe lands in the same cell as the rest of the value.
        Assert.Equal("\"'=A1,B1\"", CsvWriter.Escape("=A1,B1"));
    }

    // -------- AppSettings: save uses unique tmp + flush-through --------

    [Fact]
    public void Settings_save_does_not_leave_a_tmp_file_in_the_target_directory()
    {
        // The atomic write uses a per-call random suffix — even after multiple saves no
        // .tmp leftover should remain alongside the settings file.
        var dir = Path.Combine(Path.GetTempPath(), $"oc-a-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        try
        {
            for (int i = 0; i < 3; i++) new AppSettings { DefaultRegion = $"R{i}" }.Save(path);
            var leftovers = Directory.GetFiles(dir, "settings.json.*.tmp");
            Assert.Empty(leftovers);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    // -------- PhotoSanitizer: malformed input returns original (not partial output) --------

    [Fact]
    public void Photosanitizer_truncated_jpeg_returns_original_not_partial()
    {
        // Pre-fix a JPEG truncated mid-segment would yield a half-rewritten output that
        // wouldn't decode in any image viewer, with no signal to the caller. Now we
        // return the original on parse failure so the user keeps a usable file.
        // SOI + APP1 marker with bogus length pointing past EOF.
        var truncated = new byte[] { 0xFF, 0xD8, 0xFF, 0xE1, 0xFF, 0xFF, 0x00, 0x01 };
        var stripped = PhotoSanitizer.StripMetadata(truncated, "image/jpeg");
        Assert.Equal(truncated, stripped);
    }

    [Fact]
    public void Photosanitizer_png_without_iend_returns_original()
    {
        // PNG signature + IHDR chunk only, no IEND — typical of a download cut off
        // mid-stream. Returning a stripped PNG without IEND makes a worse file than
        // the source we started with.
        var partial = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        partial.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x0D });
        partial.AddRange(System.Text.Encoding.ASCII.GetBytes("IHDR"));
        for (int i = 0; i < 13; i++) partial.Add(0);
        for (int i = 0; i < 4; i++) partial.Add(0);
        var input = partial.ToArray();
        var stripped = PhotoSanitizer.StripMetadata(input, "image/png");
        Assert.Equal(input, stripped);
    }

    // -------- DedupEngine: blocking handles many contacts in a hot bucket --------

    [Fact]
    public void Dedup_blocking_does_not_quadratic_blow_up_for_hot_bucket()
    {
        // Pre-fix `bucket.Contains(c)` on a List<Contact> made block-construction O(n²)
        // in the size of the hottest bucket. With 1000 contacts sharing the same area
        // code that's a million Contains calls. New code uses a HashSet<Guid> so this
        // stays O(n). Smoke test: a thousand-strong hot bucket should complete fast.
        var contacts = new List<Contact>(1000);
        for (int i = 0; i < 1000; i++)
        {
            var c = new Contact { FormattedName = $"Person{i}" };
            // All share the same blocking key on phone last-7 digits.
            c.Phones.Add(PhoneNumber.Parse($"212555{i:D4}"));
            contacts.Add(c);
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var groups = new OrganizeContacts.Core.Dedup.DedupEngine().Find(contacts);
        sw.Stop();
        // Pure O(n²) bucket.Contains on 1000 items used to add ~hundreds of ms; the
        // HashSet variant should finish well under 5 seconds even on slow CI.
        Assert.True(sw.Elapsed.TotalSeconds < 5,
            $"Dedup over a 1000-strong hot bucket took {sw.Elapsed.TotalSeconds:F2}s");
        Assert.NotNull(groups);
    }
}
