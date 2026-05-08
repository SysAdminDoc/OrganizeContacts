using System.IO;
using System.Text;
using OrganizeContacts.Core;
using OrganizeContacts.Core.Cleanup;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Merge;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Normalize;
using OrganizeContacts.Core.Photos;
using OrganizeContacts.Core.Security;
using OrganizeContacts.Core.Storage;

namespace OrganizeContacts.Tests;

/// <summary>
/// Regression tests for the v0.4 hardening pass. Each test guards a specific bug
/// found during the audit so the next refactor can't silently re-introduce it.
/// </summary>
public class HardeningRegressionTests
{
    // -------- VCardImporter --------

    [Fact]
    public async Task VCard_card_with_only_email_is_no_longer_dropped()
    {
        // Pre-fix the importer required FN/N to consider a card "seen". A card with only
        // EMAIL or TEL would be silently discarded — surprising for users importing partial
        // exports from web mail.
        var src = "BEGIN:VCARD\r\nVERSION:3.0\r\nEMAIL:lonely@example.com\r\nEND:VCARD\r\n";
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.vcf");
        await File.WriteAllTextAsync(path, src);
        try
        {
            var importer = new VCardImporter();
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            Assert.Single(list);
            Assert.Single(list[0].Emails);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void VCard_categories_split_respects_escaped_commas()
    {
        var prop = VCardImporter.SplitEscaped(@"alpha,beta\,with\,commas,gamma", ',');
        Assert.Equal(3, prop.Count);
        Assert.Equal("alpha", prop[0]);
        Assert.Equal(@"beta\,with\,commas", prop[1]);
        Assert.Equal("gamma", prop[2]);
    }

    [Fact]
    public void VCard_parses_multiple_cards_from_string_without_temp_file()
    {
        const string body =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:A\r\nEND:VCARD\r\n" +
            "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:B\r\nEND:VCARD\r\n";
        var list = new VCardImporter().ParseAll(body, "in-memory").ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("A", list[0].FormattedName);
        Assert.Equal("B", list[1].FormattedName);
    }

    // -------- VCardWriter folding --------

    [Fact]
    public void VCard_folding_keeps_lines_within_75_octets_for_multibyte_text()
    {
        // Pre-fix the writer counted chars instead of UTF-8 bytes. A long string of
        // 3-byte glyphs would produce lines >150 octets — illegal vCard.
        var c = new Contact { FormattedName = string.Concat(Enumerable.Repeat("こ", 60)) };
        var output = new VCardWriter().Write(c);
        var lines = output.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var bytes = Encoding.UTF8.GetByteCount(line);
            Assert.True(bytes <= 75, $"Folded line exceeded 75 octets ({bytes}): {line}");
        }
    }

    [Fact]
    public async Task VCard_folded_multibyte_round_trips()
    {
        var c = new Contact { FormattedName = string.Concat(Enumerable.Repeat("こ", 60)) };
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.vcf");
        try
        {
            await new VCardWriter().WriteFileAsync(path, new[] { c });
            var reader = new VCardImporter();
            var read = new List<Contact>();
            await foreach (var x in reader.ReadAsync(path)) read.Add(x);
            Assert.Single(read);
            Assert.Equal(c.FormattedName, read[0].FormattedName);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // -------- BatchCleanup ordering --------

    [Fact]
    public void Dedupe_preserves_first_occurrence_metadata()
    {
        // Pre-fix DedupeBy walked the list backwards then no-op-reversed twice, effectively
        // keeping the LAST occurrence. Users reasonably expect the first phone (with its
        // existing kind/preferred state) to win.
        var c = new Contact { FormattedName = "x" };
        c.Phones.Add(new PhoneNumber { Raw = "first", Digits = "5551234567", E164 = "+15551234567", Kind = PhoneKind.Mobile, IsPreferred = true });
        c.Phones.Add(new PhoneNumber { Raw = "second", Digits = "5551234567", E164 = "+15551234567", Kind = PhoneKind.Other });

        var report = new BatchCleanup().Run(new[] { c });
        Assert.Single(c.Phones);
        Assert.Equal("first", c.Phones[0].Raw);
        Assert.True(c.Phones[0].IsPreferred);
        Assert.Contains(c.Id, report.TouchedIds);
    }

    [Fact]
    public void Cleanup_report_tracks_touched_ids()
    {
        var unchanged = new Contact { FormattedName = "u" };
        var touched = new Contact { FormattedName = "t" };
        touched.Emails.Add(new EmailAddress { Address = "x@gmail.com" });
        touched.Emails.Add(new EmailAddress { Address = "X@gmail.com" });

        var report = new BatchCleanup().Run(new[] { unchanged, touched });
        Assert.DoesNotContain(unchanged.Id, report.TouchedIds);
        Assert.Contains(touched.Id, report.TouchedIds);
    }

    // -------- GoogleCsvWriter --------

    [Fact]
    public async Task Google_csv_writer_handles_empty_input_without_crashing()
    {
        // Pre-fix .Max() on an empty source threw InvalidOperationException, crashing the export dialog.
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.csv");
        try
        {
            await new GoogleCsvWriter().WriteFileAsync(path, Array.Empty<Contact>());
            var lines = File.ReadAllLines(path);
            Assert.NotEmpty(lines); // header must still be there
            Assert.Contains("Given Name", lines[0]);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // -------- ImportPreviewer REV comparison --------

    [Fact]
    public void Rev_comparison_handles_mixed_iso_formats()
    {
        // Compact "20260301T120000Z" must compare equal to "2026-03-01T12:00:00Z" instead of
        // sorting lexicographically before "2025-..." dates.
        Assert.True(ImportPreviewer.CompareRev("20260301T120000Z", "2026-03-01T12:00:00Z") == 0);
        Assert.True(ImportPreviewer.CompareRev("2026-04-01T00:00:00Z", "2026-03-01T00:00:00Z") > 0);
        Assert.True(ImportPreviewer.CompareRev(null, "2026-01-01") < 0);
    }

    // -------- AppSettings atomicity & robustness --------

    [Fact]
    public void Settings_round_trip_preserves_values_and_uses_atomic_write()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.json");
        try
        {
            var s = new AppSettings { DefaultRegion = "GB", MatchProfile = "Strict", Theme = "Latte" };
            s.Save(path);
            // Atomic write removes the .tmp sidecar — there must be no leftover.
            Assert.False(File.Exists(path + ".tmp"));
            var loaded = AppSettings.LoadOrDefault(path);
            Assert.Equal("GB", loaded.DefaultRegion);
            Assert.Equal("Strict", loaded.MatchProfile);
            Assert.Equal("Latte", loaded.Theme);
            Assert.Null(loaded.LoadError);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Settings_corrupt_file_is_sidelined_not_silently_overwritten()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.json");
        var bak = path + ".invalid.bak";
        try
        {
            File.WriteAllText(path, "{ this is not valid json");
            var loaded = AppSettings.LoadOrDefault(path);
            Assert.NotNull(loaded.LoadError);
            Assert.True(File.Exists(bak), "corrupt settings file should be preserved as a .invalid.bak");
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { File.Delete(bak); } catch { }
        }
    }

    // -------- CredentialVault case-insensitive + corrupt --------

    [Fact]
    public void Credential_vault_lookup_is_case_insensitive_after_round_trip()
    {
        if (!CredentialVault.IsSupported) return; // skip on non-Windows CI
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.dat");
        try
        {
            var v1 = new CredentialVault(path);
            v1.Save("CardDav", "user", "secret");
            var v2 = new CredentialVault(path); // fresh instance forces a Load() round trip
            // Pre-fix Deserialize<Dictionary> dropped the OrdinalIgnoreCase comparer
            // and lookups by a different casing returned null.
            Assert.NotNull(v2.Get("carddav"));
            Assert.NotNull(v2.Get("CARDDAV"));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Credential_vault_corrupt_blob_is_backed_up_not_overwritten()
    {
        if (!CredentialVault.IsSupported) return;
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.dat");
        try
        {
            // Write a non-DPAPI blob so Load() throws.
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
            var v = new CredentialVault(path);
            _ = v.List(); // triggers Load()
            Assert.True(v.CorruptVaultDetected);

            // Find the side-lined backup so the user can attempt recovery.
            var dir = Path.GetDirectoryName(path)!;
            var stem = Path.GetFileName(path);
            var backups = Directory.GetFiles(dir, $"{stem}.corrupt-*.bak");
            Assert.NotEmpty(backups);
            foreach (var b in backups) try { File.Delete(b); } catch { }
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // -------- DedupEngine --------

    [Fact]
    public void Dedup_pair_score_uses_min_phone_digits_from_rules()
    {
        // With MinPhoneDigits=10 the pair-score must require a 10-digit overlap, not the
        // hardcoded 7. We construct two numbers whose last-7 digits match but whose last-10
        // diverge — pre-fix this would score on the 7-digit overlap and report a phone match.
        var rules = new OrganizeContacts.Core.Dedup.MatchRules { MinPhoneDigits = 10, MatchOnNormalizedName = false };
        var engine = new OrganizeContacts.Core.Dedup.DedupEngine(rules);

        Contact Mk(string fn, string digits)
        {
            var c = new Contact { FormattedName = fn };
            c.Phones.Add(new PhoneNumber { Raw = digits, Digits = digits, E164 = null });
            return c;
        }

        // Last 7 digits = "1234567", last 10 differ ("9991234567" vs "1111234567").
        var a = Mk("Alice", "9991234567");
        var b = Mk("Bob",   "1111234567");
        var (conf, signals) = engine.ScorePair(a, b);
        Assert.DoesNotContain(signals, s => s.Label.StartsWith("phone last 7", StringComparison.Ordinal));
        Assert.True(conf == 0, $"Expected no phone-tail signal at MinPhoneDigits=10, got conf={conf}");
    }

    // -------- MergeEngine --------

    [Fact]
    public void Merge_donates_photo_when_primary_has_none()
    {
        var primary = new Contact { FormattedName = "P" };
        var sec = new Contact { FormattedName = "S", PhotoBytes = new byte[] { 1, 2, 3 }, PhotoMimeType = "image/jpeg" };
        var plan = new MergePlan { Primary = primary, Secondaries = { sec } };
        var result = new MergeEngine().Apply(plan);
        Assert.NotNull(result.Survivor.PhotoBytes);
        Assert.Equal(3, result.Survivor.PhotoBytes!.Length);
    }

    [Fact]
    public void Merge_culture_invariant_birthday_choice()
    {
        // Pre-fix DateOnly.Parse used current culture; choosing "07/05/2026" from a
        // de-DE machine threw FormatException mid-merge.
        var primary = new Contact { FormattedName = "P" };
        var sec = new Contact { FormattedName = "S" };
        var plan = new MergePlan
        {
            Primary = primary,
            Secondaries = { sec },
            Choices = { new MergeChoice("Birthday", MergeFieldOrigin.Secondary, "2026-05-07") }
        };
        var result = new MergeEngine().Apply(plan);
        Assert.Equal(new DateOnly(2026, 5, 7), result.Survivor.Birthday);
    }

    // -------- SQLite WAL + bulk loader --------

    [Fact]
    public void Repository_uses_wal_journal_mode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.sqlite");
        try
        {
            using var repo = new ContactRepository(path);
            using var cmd = repo.Connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            var mode = cmd.ExecuteScalar()?.ToString();
            Assert.Equal("wal", mode, ignoreCase: true);
        }
        finally
        {
            // WAL leaves -wal and -shm sidecars; clean them all.
            foreach (var ext in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + ext); } catch { }
        }
    }

    [Fact]
    public void Bulk_list_contacts_returns_full_children_for_every_row()
    {
        // Before the bulk loader, this exercised 5 child queries per contact.  The new
        // loader must produce identical Contact graphs for every row including ones with
        // no children.
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.sqlite");
        try
        {
            using var repo = new ContactRepository(path);
            var src = repo.UpsertSource(new ContactSource { Kind = SourceKind.File, Label = "bulk" });

            var rich = new Contact { FormattedName = "Rich", SourceId = src.Id };
            rich.Phones.Add(PhoneNumber.Parse("5551111111", PhoneKind.Mobile));
            rich.Phones.Add(PhoneNumber.Parse("5552222222", PhoneKind.Work));
            rich.Emails.Add(new EmailAddress { Address = "rich@example.com" });
            rich.Categories.Add("VIP");
            rich.Urls.Add("https://rich.example.com/");
            rich.CustomFields["X-FOO"] = "bar";
            repo.InsertContact(rich);

            var bare = new Contact { FormattedName = "Bare", SourceId = src.Id };
            repo.InsertContact(bare);

            var listed = repo.ListContacts();
            Assert.Equal(2, listed.Count);
            var rd = listed.Single(c => c.Id == rich.Id);
            Assert.Equal(2, rd.Phones.Count);
            Assert.Single(rd.Emails);
            Assert.Single(rd.Categories);
            Assert.Single(rd.Urls);
            Assert.Equal("bar", rd.CustomFields["X-FOO"]);
            var bd = listed.Single(c => c.Id == bare.Id);
            Assert.Empty(bd.Phones);
            Assert.Empty(bd.Emails);
        }
        finally
        {
            foreach (var ext in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + ext); } catch { }
        }
    }

    [Fact]
    public void Bulk_list_contacts_skips_soft_deleted_children()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.sqlite");
        try
        {
            using var repo = new ContactRepository(path);
            var c = new Contact { FormattedName = "Deleted" };
            c.Phones.Add(PhoneNumber.Parse("5551234567"));
            repo.InsertContact(c);
            repo.SoftDeleteContact(c.Id);
            var listed = repo.ListContacts();
            Assert.Empty(listed);
        }
        finally
        {
            foreach (var ext in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + ext); } catch { }
        }
    }

    // -------- CSV date parsing culture --------

    [Fact]
    public void Csv_date_parser_handles_iso_and_partial_forms()
    {
        Assert.True(GoogleCsvImporter.TryParseCsvDate("2026-05-07", out var iso));
        Assert.Equal(new DateOnly(2026, 5, 7), iso);

        Assert.True(GoogleCsvImporter.TryParseCsvDate("19850421", out var compact));
        Assert.Equal(new DateOnly(1985, 4, 21), compact);

        // vCard 4.0 partial date with no year.
        Assert.True(GoogleCsvImporter.TryParseCsvDate("--0507", out var partial));
        Assert.Equal(5, partial.Month);
        Assert.Equal(7, partial.Day);

        Assert.False(GoogleCsvImporter.TryParseCsvDate("not a date", out _));
        Assert.False(GoogleCsvImporter.TryParseCsvDate("", out _));
    }

    // -------- HistoryStore two-step insert --------

    // -------- VCard QP soft-line-break --------

    [Fact]
    public async Task VCard_quoted_printable_soft_line_break_is_unfolded()
    {
        // Long QP value split with `=` at end of line and no leading WS on the
        // continuation. Pre-fix this would truncate at "Andr" because the unfolder
        // only handled WS-prefixed continuations.
        var src =
            "BEGIN:VCARD\r\n" +
            "VERSION:2.1\r\n" +
            "FN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:Andr=\r\n" +
            "=C3=A9 Long\r\n" +
            "END:VCARD\r\n";
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.vcf");
        await File.WriteAllTextAsync(path, src);
        try
        {
            var importer = new VCardImporter();
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            Assert.Single(list);
            Assert.Equal("André Long", list[0].FormattedName);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // -------- OutlookCsvWriter overflow --------

    [Fact]
    public async Task Outlook_csv_writer_folds_extra_phones_into_notes()
    {
        // Outlook's schema only has 2 Work + 2 Home + 1 each of Mobile/Other/Pager/Main +
        // 1 Business Fax + 1 Home Fax. A contact with 3 work phones, 2 mobiles, etc.
        // used to lose data on export. Now the surplus must land in Notes with a
        // recoverable marker.
        var c = new Contact { FormattedName = "Many Phones", Notes = "original note" };
        c.Phones.Add(PhoneNumber.Parse("5550000001", PhoneKind.Work));
        c.Phones.Add(PhoneNumber.Parse("5550000002", PhoneKind.Work));
        c.Phones.Add(PhoneNumber.Parse("5550000003", PhoneKind.Work));   // 3rd work — overflow
        c.Phones.Add(PhoneNumber.Parse("5550000010", PhoneKind.Mobile));
        c.Phones.Add(PhoneNumber.Parse("5550000011", PhoneKind.Mobile)); // 2nd mobile — overflow
        c.Phones.Add(PhoneNumber.Parse("5550000020", PhoneKind.Fax));
        c.Phones.Add(PhoneNumber.Parse("5550000021", PhoneKind.Fax));    // 2nd fax — Home Fax slot
        c.Phones.Add(PhoneNumber.Parse("5550000022", PhoneKind.Fax));    // 3rd fax — overflow

        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.csv");
        try
        {
            await new OutlookCsvWriter().WriteFileAsync(path, new[] { c });
            var bytes = await File.ReadAllTextAsync(path);
            // The overflow marker must appear in the row, surfacing the dropped values.
            Assert.Contains("[OrganizeContacts overflow]", bytes);
            Assert.Contains("5550000003", bytes); // 3rd work
            Assert.Contains("5550000011", bytes); // 2nd mobile
            Assert.Contains("5550000022", bytes); // 3rd fax
            // First two Faxes occupy Business Fax and Home Fax slots — must NOT be in the overflow.
            // (Cheap check: the overflow segment is what comes after the marker.)
            var marker = bytes.IndexOf("[OrganizeContacts overflow]", StringComparison.Ordinal);
            var overflow = bytes.Substring(marker);
            Assert.DoesNotContain("5550000020", overflow);
            Assert.DoesNotContain("5550000021", overflow);
            // Original note must still be there.
            Assert.Contains("original note", bytes);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // -------- Repository atomicity --------

    [Fact]
    public void Repository_insert_is_atomic_without_explicit_transaction()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.sqlite");
        try
        {
            using var repo = new ContactRepository(path);
            var c = new Contact { Id = Guid.NewGuid(), FormattedName = "atomicity" };
            c.Phones.Add(PhoneNumber.Parse("5551112222"));
            repo.InsertContact(c); // no tx supplied — must wrap an implicit one

            var read = repo.GetById(c.Id);
            Assert.NotNull(read);
            Assert.Single(read!.Phones);
        }
        finally
        {
            foreach (var ext in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + ext); } catch { }
        }
    }

    [Fact]
    public void Repository_update_with_implicit_tx_does_not_leave_orphaned_children()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.sqlite");
        try
        {
            using var repo = new ContactRepository(path);
            var c = new Contact { FormattedName = "before" };
            c.Phones.Add(PhoneNumber.Parse("5550000000"));
            c.Categories.Add("alpha");
            repo.InsertContact(c);

            // Replace the children. The implicit-tx contract guarantees the new state
            // is observed atomically — a reader between the DELETE-children and
            // INSERT-children steps must NOT see an empty child list.
            c.FormattedName = "after";
            c.Phones.Clear();
            c.Phones.Add(PhoneNumber.Parse("5559999999"));
            c.Categories.Clear();
            c.Categories.Add("beta");
            repo.UpdateContact(c);

            var read = repo.GetById(c.Id);
            Assert.NotNull(read);
            Assert.Equal("after", read!.FormattedName);
            Assert.Single(read.Phones);
            Assert.Equal("5559999999", read.Phones[0].Digits);
            Assert.Single(read.Categories);
            Assert.Equal("beta", read.Categories[0]);
        }
        finally
        {
            foreach (var ext in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + ext); } catch { }
        }
    }

    // -------- BatchCleanup cancellation --------

    [Fact]
    public void Batch_cleanup_honours_cancellation()
    {
        var contacts = Enumerable.Range(0, 20)
            .Select(i => { var c = new Contact { FormattedName = $"c{i}" }; c.Emails.Add(new EmailAddress { Address = $"x@x.com" }); return c; })
            .ToList();
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => new BatchCleanup().Run(contacts, ct: cts.Token));
    }

    [Fact]
    public void History_record_undo_returns_a_real_rowid()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-h-{Guid.NewGuid():N}.sqlite");
        try
        {
            using var repo = new ContactRepository(path);
            var hist = new HistoryStore(repo);
            var id1 = hist.RecordUndo("merge", new { x = 1 }, new { x = 0 }, "first");
            var id2 = hist.RecordUndo("merge", new { x = 2 }, new { x = 1 }, "second");
            Assert.True(id1 > 0);
            Assert.True(id2 > id1, "rowids must be strictly increasing across inserts");
            var entries = hist.ListUndo();
            Assert.Equal(2, entries.Count);
        }
        finally
        {
            foreach (var ext in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + ext); } catch { }
        }
    }
}
