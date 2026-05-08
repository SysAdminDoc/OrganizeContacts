using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Storage;

public sealed class ContactRepository : IDisposable
{
    private readonly SqliteConnection _conn;

    public ContactRepository(string dbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _conn = new SqliteConnection($"Data Source={dbPath};Foreign Keys=True");
        _conn.Open();
        ApplyConnectionPragmas(_conn);
        Migrations.Apply(_conn);
    }

    /// <summary>Connection-level pragmas applied on every open. WAL gives us non-blocking
    /// reads while a write is in flight (so a UI-thread ListContacts doesn't stall behind a
    /// background import), `synchronous=NORMAL` is the recommended pair for desktop-grade
    /// crash safety, and `busy_timeout` makes the few writes that DO contend fail with a
    /// retry-friendly delay instead of an immediate `SQLITE_BUSY`.</summary>
    private static void ApplyConnectionPragmas(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
    }

    public SqliteConnection Connection => _conn;

    public ContactSource UpsertSource(ContactSource src)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sources (id, kind, label, file_path, account, created_utc)
            VALUES ($id, $kind, $label, $fp, $acct, $ts)
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind,
                label = excluded.label,
                file_path = excluded.file_path,
                account = excluded.account;
            """;
        cmd.Parameters.AddWithValue("$id", src.Id.ToString());
        cmd.Parameters.AddWithValue("$kind", src.Kind.ToString());
        cmd.Parameters.AddWithValue("$label", src.Label);
        cmd.Parameters.AddWithValue("$fp", (object?)src.FilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$acct", (object?)src.Account ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", src.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();
        return src;
    }

    public IReadOnlyList<ContactSource> ListSources()
    {
        var list = new List<ContactSource>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, kind, label, file_path, account, created_utc FROM sources ORDER BY created_utc DESC;";
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            list.Add(new ContactSource
            {
                Id = Guid.Parse(rdr.GetString(0)),
                Kind = Enum.TryParse<SourceKind>(rdr.GetString(1), out var k) ? k : SourceKind.Unknown,
                Label = rdr.GetString(2),
                FilePath = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                Account = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                CreatedAt = DateTimeOffset.Parse(rdr.GetString(5), CultureInfo.InvariantCulture),
            });
        }
        return list;
    }

    public ImportRecord StartImport(ImportRecord rec)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO imports (id, source_id, file_path, started_utc, status, contacts_created, contacts_updated, contacts_skipped, notes)
            VALUES ($id, $src, $fp, $st, $status, 0, 0, 0, $notes);
            """;
        cmd.Parameters.AddWithValue("$id", rec.Id.ToString());
        cmd.Parameters.AddWithValue("$src", rec.SourceId.ToString());
        cmd.Parameters.AddWithValue("$fp", rec.FilePath);
        cmd.Parameters.AddWithValue("$st", rec.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$status", rec.Status.ToString());
        cmd.Parameters.AddWithValue("$notes", (object?)rec.Notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return rec;
    }

    public void FinishImport(ImportRecord rec)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE imports SET
                finished_utc = $end,
                status = $status,
                contacts_created = $cre,
                contacts_updated = $upd,
                contacts_skipped = $skp,
                notes = $notes
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", rec.Id.ToString());
        cmd.Parameters.AddWithValue("$end", (rec.FinishedAt ?? DateTimeOffset.UtcNow).ToString("O"));
        cmd.Parameters.AddWithValue("$status", rec.Status.ToString());
        cmd.Parameters.AddWithValue("$cre", rec.ContactsCreated);
        cmd.Parameters.AddWithValue("$upd", rec.ContactsUpdated);
        cmd.Parameters.AddWithValue("$skp", rec.ContactsSkipped);
        cmd.Parameters.AddWithValue("$notes", (object?)rec.Notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<ImportRecord> ListImports()
    {
        var list = new List<ImportRecord>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, source_id, file_path, started_utc, finished_utc, status,
                   contacts_created, contacts_updated, contacts_skipped, notes
            FROM imports ORDER BY started_utc DESC;
            """;
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            list.Add(new ImportRecord
            {
                Id = Guid.Parse(rdr.GetString(0)),
                SourceId = Guid.Parse(rdr.GetString(1)),
                FilePath = rdr.GetString(2),
                StartedAt = DateTimeOffset.Parse(rdr.GetString(3), CultureInfo.InvariantCulture),
                FinishedAt = rdr.IsDBNull(4) ? null : DateTimeOffset.Parse(rdr.GetString(4), CultureInfo.InvariantCulture),
                Status = Enum.TryParse<ImportStatus>(rdr.GetString(5), out var s) ? s : ImportStatus.Pending,
                ContactsCreated = rdr.GetInt32(6),
                ContactsUpdated = rdr.GetInt32(7),
                ContactsSkipped = rdr.GetInt32(8),
                Notes = rdr.IsDBNull(9) ? null : rdr.GetString(9),
            });
        }
        return list;
    }

    public Contact? FindByUid(string uid, Guid? sourceId = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sourceId.HasValue
            ? "SELECT id FROM contacts WHERE uid = $uid AND source_id = $src AND deleted_utc IS NULL LIMIT 1;"
            : "SELECT id FROM contacts WHERE uid = $uid AND deleted_utc IS NULL LIMIT 1;";
        cmd.Parameters.AddWithValue("$uid", uid);
        if (sourceId.HasValue) cmd.Parameters.AddWithValue("$src", sourceId.Value.ToString());
        var v = cmd.ExecuteScalar();
        if (v is null or DBNull) return null;
        return GetById(Guid.Parse((string)v));
    }

    public Contact? GetById(Guid id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, source_id, import_id, uid, rev, formatted_name, given_name, family_name,
                   additional_names, honorific_prefix, honorific_suffix, nickname, organization,
                   title, birthday, anniversary, notes, photo_bytes, photo_mime, source_file,
                   source_format, imported_utc, updated_utc, custom_fields_json
            FROM contacts WHERE id = $id AND deleted_utc IS NULL;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;
        var c = ReadContactRow(rdr);
        rdr.Close();
        LoadChildren(c);
        return c;
    }

    /// <summary>True iff a row with this id exists in the contacts table, even when soft-deleted.
    /// Use to decide between INSERT and UPDATE during restore — INSERT-over-soft-deleted-row would
    /// throw a primary-key conflict.</summary>
    public bool ExistsAnyState(Guid id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM contacts WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        return cmd.ExecuteScalar() is not null;
    }

    public IReadOnlyList<Contact> ListContacts()
    {
        var list = new List<Contact>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, source_id, import_id, uid, rev, formatted_name, given_name, family_name,
                       additional_names, honorific_prefix, honorific_suffix, nickname, organization,
                       title, birthday, anniversary, notes, photo_bytes, photo_mime, source_file,
                       source_format, imported_utc, updated_utc, custom_fields_json
                FROM contacts WHERE deleted_utc IS NULL ORDER BY formatted_name COLLATE NOCASE;
                """;
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read()) list.Add(ReadContactRow(rdr));
        }

        // N+1 → bulk load: previously each contact ran 5 child queries (phones, emails,
        // addresses, urls, categories), so 5,000 contacts = 25,000 round trips.  Now we
        // run 5 total scans and bucket by contact_id, dropping it to a near-flat cost.
        if (list.Count == 0) return list;
        var byId = new Dictionary<Guid, Contact>(list.Count);
        foreach (var c in list) byId[c.Id] = c;
        BulkLoadChildren(byId);
        return list;
    }

    /// <summary>Scan each child table once and dispatch rows into the matching parent contact.
    /// Filters on `deleted_utc IS NULL` so soft-deleted parents never receive children.</summary>
    private void BulkLoadChildren(Dictionary<Guid, Contact> byId)
    {
        // Phones
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT p.contact_id, p.raw, p.digits, p.e164, p.kind, p.is_preferred, p.source_id
                FROM phones p
                JOIN contacts c ON c.id = p.contact_id AND c.deleted_utc IS NULL;
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!byId.TryGetValue(Guid.Parse(r.GetString(0)), out var c)) continue;
                c.Phones.Add(new PhoneNumber
                {
                    Raw = r.GetString(1),
                    Digits = r.GetString(2),
                    E164 = r.IsDBNull(3) ? null : r.GetString(3),
                    Kind = Enum.TryParse<PhoneKind>(r.GetString(4), out var k) ? k : PhoneKind.Other,
                    IsPreferred = r.GetInt32(5) != 0,
                    SourceId = r.IsDBNull(6) ? null : Guid.Parse(r.GetString(6)),
                });
            }
        }

        // Emails
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT e.contact_id, e.address, e.canonical, e.kind, e.is_preferred, e.source_id
                FROM emails e
                JOIN contacts c ON c.id = e.contact_id AND c.deleted_utc IS NULL;
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!byId.TryGetValue(Guid.Parse(r.GetString(0)), out var c)) continue;
                c.Emails.Add(new EmailAddress
                {
                    Address = r.GetString(1),
                    CanonicalOverride = r.IsDBNull(2) ? null : r.GetString(2),
                    Kind = Enum.TryParse<EmailKind>(r.GetString(3), out var k) ? k : EmailKind.Other,
                    IsPreferred = r.GetInt32(4) != 0,
                    SourceId = r.IsDBNull(5) ? null : Guid.Parse(r.GetString(5)),
                });
            }
        }

        // Addresses
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT a.contact_id, a.po_box, a.extended, a.street, a.locality, a.region,
                       a.postal_code, a.country, a.kind, a.source_id
                FROM addresses a
                JOIN contacts c ON c.id = a.contact_id AND c.deleted_utc IS NULL;
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!byId.TryGetValue(Guid.Parse(r.GetString(0)), out var c)) continue;
                c.Addresses.Add(new PostalAddress
                {
                    PoBox = r.IsDBNull(1) ? null : r.GetString(1),
                    Extended = r.IsDBNull(2) ? null : r.GetString(2),
                    Street = r.IsDBNull(3) ? null : r.GetString(3),
                    Locality = r.IsDBNull(4) ? null : r.GetString(4),
                    Region = r.IsDBNull(5) ? null : r.GetString(5),
                    PostalCode = r.IsDBNull(6) ? null : r.GetString(6),
                    Country = r.IsDBNull(7) ? null : r.GetString(7),
                    Kind = Enum.TryParse<AddressKind>(r.GetString(8), out var k) ? k : AddressKind.Other,
                    SourceId = r.IsDBNull(9) ? null : Guid.Parse(r.GetString(9)),
                });
            }
        }

        // URLs
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT u.contact_id, u.value FROM urls u
                JOIN contacts c ON c.id = u.contact_id AND c.deleted_utc IS NULL;
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!byId.TryGetValue(Guid.Parse(r.GetString(0)), out var c)) continue;
                c.Urls.Add(r.GetString(1));
            }
        }

        // Categories
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT g.contact_id, g.value FROM categories g
                JOIN contacts c ON c.id = g.contact_id AND c.deleted_utc IS NULL;
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!byId.TryGetValue(Guid.Parse(r.GetString(0)), out var c)) continue;
                c.Categories.Add(r.GetString(1));
            }
        }
    }

    private static Contact ReadContactRow(SqliteDataReader rdr)
    {
        var c = new Contact
        {
            Id = Guid.Parse(rdr.GetString(0)),
            SourceId = rdr.IsDBNull(1) ? null : Guid.Parse(rdr.GetString(1)),
            ImportId = rdr.IsDBNull(2) ? null : Guid.Parse(rdr.GetString(2)),
            Uid = rdr.IsDBNull(3) ? null : rdr.GetString(3),
            Rev = rdr.IsDBNull(4) ? null : rdr.GetString(4),
            FormattedName = rdr.IsDBNull(5) ? null : rdr.GetString(5),
            GivenName = rdr.IsDBNull(6) ? null : rdr.GetString(6),
            FamilyName = rdr.IsDBNull(7) ? null : rdr.GetString(7),
            AdditionalNames = rdr.IsDBNull(8) ? null : rdr.GetString(8),
            HonorificPrefix = rdr.IsDBNull(9) ? null : rdr.GetString(9),
            HonorificSuffix = rdr.IsDBNull(10) ? null : rdr.GetString(10),
            Nickname = rdr.IsDBNull(11) ? null : rdr.GetString(11),
            Organization = rdr.IsDBNull(12) ? null : rdr.GetString(12),
            Title = rdr.IsDBNull(13) ? null : rdr.GetString(13),
            Birthday = rdr.IsDBNull(14) ? null : DateOnly.Parse(rdr.GetString(14), CultureInfo.InvariantCulture),
            Anniversary = rdr.IsDBNull(15) ? null : DateOnly.Parse(rdr.GetString(15), CultureInfo.InvariantCulture),
            Notes = rdr.IsDBNull(16) ? null : rdr.GetString(16),
            PhotoBytes = rdr.IsDBNull(17) ? null : (byte[])rdr["photo_bytes"],
            PhotoMimeType = rdr.IsDBNull(18) ? null : rdr.GetString(18),
            SourceFile = rdr.IsDBNull(19) ? null : rdr.GetString(19),
            SourceFormat = rdr.IsDBNull(20) ? null : rdr.GetString(20),
            ImportedAt = DateTimeOffset.Parse(rdr.GetString(21), CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(rdr.GetString(22), CultureInfo.InvariantCulture),
        };

        if (!rdr.IsDBNull(23))
        {
            try
            {
                var json = rdr.GetString(23);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict is not null) foreach (var kv in dict) c.CustomFields[kv.Key] = kv.Value;
            }
            catch { /* tolerate corrupt custom-field json */ }
        }
        return c;
    }

    private void LoadChildren(Contact c)
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT raw, digits, e164, kind, is_preferred, source_id FROM phones WHERE contact_id = $id;";
            cmd.Parameters.AddWithValue("$id", c.Id.ToString());
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                c.Phones.Add(new PhoneNumber
                {
                    Raw = r.GetString(0),
                    Digits = r.GetString(1),
                    E164 = r.IsDBNull(2) ? null : r.GetString(2),
                    Kind = Enum.TryParse<PhoneKind>(r.GetString(3), out var k) ? k : PhoneKind.Other,
                    IsPreferred = r.GetInt32(4) != 0,
                    SourceId = r.IsDBNull(5) ? null : Guid.Parse(r.GetString(5)),
                });
            }
        }
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT address, canonical, kind, is_preferred, source_id FROM emails WHERE contact_id = $id;";
            cmd.Parameters.AddWithValue("$id", c.Id.ToString());
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                c.Emails.Add(new EmailAddress
                {
                    Address = r.GetString(0),
                    CanonicalOverride = r.IsDBNull(1) ? null : r.GetString(1),
                    Kind = Enum.TryParse<EmailKind>(r.GetString(2), out var k) ? k : EmailKind.Other,
                    IsPreferred = r.GetInt32(3) != 0,
                    SourceId = r.IsDBNull(4) ? null : Guid.Parse(r.GetString(4)),
                });
            }
        }
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT po_box, extended, street, locality, region, postal_code, country, kind, source_id FROM addresses WHERE contact_id = $id;";
            cmd.Parameters.AddWithValue("$id", c.Id.ToString());
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                c.Addresses.Add(new PostalAddress
                {
                    PoBox = r.IsDBNull(0) ? null : r.GetString(0),
                    Extended = r.IsDBNull(1) ? null : r.GetString(1),
                    Street = r.IsDBNull(2) ? null : r.GetString(2),
                    Locality = r.IsDBNull(3) ? null : r.GetString(3),
                    Region = r.IsDBNull(4) ? null : r.GetString(4),
                    PostalCode = r.IsDBNull(5) ? null : r.GetString(5),
                    Country = r.IsDBNull(6) ? null : r.GetString(6),
                    Kind = Enum.TryParse<AddressKind>(r.GetString(7), out var k) ? k : AddressKind.Other,
                    SourceId = r.IsDBNull(8) ? null : Guid.Parse(r.GetString(8)),
                });
            }
        }
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM urls WHERE contact_id = $id;";
            cmd.Parameters.AddWithValue("$id", c.Id.ToString());
            using var r = cmd.ExecuteReader();
            while (r.Read()) c.Urls.Add(r.GetString(0));
        }
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM categories WHERE contact_id = $id;";
            cmd.Parameters.AddWithValue("$id", c.Id.ToString());
            using var r = cmd.ExecuteReader();
            while (r.Read()) c.Categories.Add(r.GetString(0));
        }
    }

    public void InsertContact(Contact c, SqliteTransaction? tx = null)
    {
        // If the caller didn't supply a transaction, wrap the parent INSERT and the child
        // ReplaceChildren in our own one — otherwise a SQL failure mid-loop would leave a
        // parent row with stale-or-missing children.
        var ownsTx = tx is null;
        var implicitTx = ownsTx ? _conn.BeginTransaction() : null;
        var effective = tx ?? implicitTx!;
        try
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = effective;
                cmd.CommandText = """
                    INSERT INTO contacts (
                        id, source_id, import_id, uid, rev, formatted_name, given_name, family_name,
                        additional_names, honorific_prefix, honorific_suffix, nickname, organization,
                        title, birthday, anniversary, notes, photo_bytes, photo_mime, source_file,
                        source_format, imported_utc, updated_utc, custom_fields_json
                    ) VALUES (
                        $id, $src, $imp, $uid, $rev, $fn, $gn, $fam,
                        $add, $hp, $hs, $nick, $org,
                        $title, $bday, $anniv, $notes, $photo, $pmime, $sfile,
                        $sfmt, $imported, $updated, $custom
                    );
                    """;
                BindContactParams(cmd, c);
                cmd.ExecuteNonQuery();
            }
            ReplaceChildren(c, effective);
            implicitTx?.Commit();
        }
        catch
        {
            implicitTx?.Rollback();
            throw;
        }
        finally
        {
            implicitTx?.Dispose();
        }
    }

    public void UpdateContact(Contact c, SqliteTransaction? tx = null)
    {
        var ownsTx = tx is null;
        var implicitTx = ownsTx ? _conn.BeginTransaction() : null;
        var effective = tx ?? implicitTx!;
        try
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = effective;
                cmd.CommandText = """
                    UPDATE contacts SET
                        source_id = $src,
                        import_id = $imp,
                        uid = $uid,
                        rev = $rev,
                        formatted_name = $fn,
                        given_name = $gn,
                        family_name = $fam,
                        additional_names = $add,
                        honorific_prefix = $hp,
                        honorific_suffix = $hs,
                        nickname = $nick,
                        organization = $org,
                        title = $title,
                        birthday = $bday,
                        anniversary = $anniv,
                        notes = $notes,
                        photo_bytes = $photo,
                        photo_mime = $pmime,
                        source_file = $sfile,
                        source_format = $sfmt,
                        updated_utc = $updated,
                        custom_fields_json = $custom
                    WHERE id = $id;
                    """;
                BindContactParams(cmd, c);
                cmd.ExecuteNonQuery();
            }
            ReplaceChildren(c, effective);
            implicitTx?.Commit();
        }
        catch
        {
            implicitTx?.Rollback();
            throw;
        }
        finally
        {
            implicitTx?.Dispose();
        }
    }

    public void SoftDeleteContact(Guid id, SqliteTransaction? tx = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE contacts SET deleted_utc = $ts WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void RestoreContact(Guid id, SqliteTransaction? tx = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE contacts SET deleted_utc = NULL WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }

    public void HardDeleteContact(Guid id, SqliteTransaction? tx = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM contacts WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }

    private void BindContactParams(SqliteCommand cmd, Contact c)
    {
        cmd.Parameters.AddWithValue("$id", c.Id.ToString());
        cmd.Parameters.AddWithValue("$src", (object?)c.SourceId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$imp", (object?)c.ImportId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$uid", (object?)c.Uid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rev", (object?)c.Rev ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fn", (object?)c.FormattedName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$gn", (object?)c.GivenName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fam", (object?)c.FamilyName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$add", (object?)c.AdditionalNames ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hp", (object?)c.HonorificPrefix ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hs", (object?)c.HonorificSuffix ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$nick", (object?)c.Nickname ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$org", (object?)c.Organization ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", (object?)c.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bday", (object?)c.Birthday?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$anniv", (object?)c.Anniversary?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)c.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$photo", (object?)c.PhotoBytes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pmime", (object?)c.PhotoMimeType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sfile", (object?)c.SourceFile ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sfmt", (object?)c.SourceFormat ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$imported", c.ImportedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", c.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$custom",
            c.CustomFields.Count == 0 ? (object)DBNull.Value
                : JsonSerializer.Serialize(c.CustomFields));
    }

    private void ReplaceChildren(Contact c, SqliteTransaction? tx)
    {
        DeleteChildren(c.Id, tx);

        foreach (var p in c.Phones)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO phones (contact_id, raw, digits, e164, kind, is_preferred, source_id)
                VALUES ($cid, $raw, $digits, $e164, $kind, $pref, $src);
                """;
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString());
            cmd.Parameters.AddWithValue("$raw", p.Raw);
            cmd.Parameters.AddWithValue("$digits", p.Digits);
            cmd.Parameters.AddWithValue("$e164", (object?)p.E164 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$kind", p.Kind.ToString());
            cmd.Parameters.AddWithValue("$pref", p.IsPreferred ? 1 : 0);
            cmd.Parameters.AddWithValue("$src", (object?)p.SourceId?.ToString() ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        foreach (var e in c.Emails)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO emails (contact_id, address, canonical, kind, is_preferred, source_id)
                VALUES ($cid, $addr, $canon, $kind, $pref, $src);
                """;
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString());
            cmd.Parameters.AddWithValue("$addr", e.Address);
            cmd.Parameters.AddWithValue("$canon", e.Canonical);
            cmd.Parameters.AddWithValue("$kind", e.Kind.ToString());
            cmd.Parameters.AddWithValue("$pref", e.IsPreferred ? 1 : 0);
            cmd.Parameters.AddWithValue("$src", (object?)e.SourceId?.ToString() ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        foreach (var a in c.Addresses)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO addresses (contact_id, po_box, extended, street, locality, region, postal_code, country, kind, source_id)
                VALUES ($cid, $po, $ext, $st, $loc, $reg, $pc, $ctry, $kind, $src);
                """;
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString());
            cmd.Parameters.AddWithValue("$po", (object?)a.PoBox ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ext", (object?)a.Extended ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$st", (object?)a.Street ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$loc", (object?)a.Locality ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$reg", (object?)a.Region ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pc", (object?)a.PostalCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ctry", (object?)a.Country ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$kind", a.Kind.ToString());
            cmd.Parameters.AddWithValue("$src", (object?)a.SourceId?.ToString() ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        foreach (var u in c.Urls)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO urls (contact_id, value) VALUES ($cid, $v);";
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString());
            cmd.Parameters.AddWithValue("$v", u);
            cmd.ExecuteNonQuery();
        }

        foreach (var cat in c.Categories)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO categories (contact_id, value) VALUES ($cid, $v);";
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString());
            cmd.Parameters.AddWithValue("$v", cat);
            cmd.ExecuteNonQuery();
        }
    }

    private void DeleteChildren(Guid contactId, SqliteTransaction? tx)
    {
        foreach (var t in new[] { "phones", "emails", "addresses", "urls", "categories" })
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {t} WHERE contact_id = $id;";
            cmd.Parameters.AddWithValue("$id", contactId.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    public SqliteTransaction BeginTransaction() => _conn.BeginTransaction();

    public void Audit(string op, Guid? contactId = null, string? payload = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO audit_log (ts_utc, op, contact_id, payload)
            VALUES ($ts, $op, $cid, $payload);
            """;
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$op", op);
        cmd.Parameters.AddWithValue("$cid", (object?)contactId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$payload", (object?)payload ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _conn.Dispose();
}
