using Microsoft.Data.Sqlite;

namespace OrganizeContacts.Core.Storage;

internal static class Migrations
{
    public static int CurrentVersion => 1;

    public static void Apply(SqliteConnection conn)
    {
        EnsureMetaTable(conn);
        var current = ReadVersion(conn);

        if (current < 1)
        {
            using var tx = conn.BeginTransaction();
            ExecBatch(conn, tx, V1);
            WriteVersion(conn, tx, 1);
            tx.Commit();
        }
    }

    private static void EnsureMetaTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_version (
                version    INTEGER PRIMARY KEY,
                applied_utc TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static int ReadVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? 0 : Convert.ToInt32(v);
    }

    private static void WriteVersion(SqliteConnection conn, SqliteTransaction tx, int v)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO schema_version(version, applied_utc) VALUES ($v, $ts);";
        cmd.Parameters.AddWithValue("$v", v);
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void ExecBatch(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private const string V1 = """
        CREATE TABLE sources (
            id          TEXT PRIMARY KEY,
            kind        TEXT NOT NULL,
            label       TEXT NOT NULL,
            file_path   TEXT,
            account     TEXT,
            created_utc TEXT NOT NULL
        );

        CREATE TABLE imports (
            id              TEXT PRIMARY KEY,
            source_id       TEXT NOT NULL,
            file_path       TEXT NOT NULL,
            started_utc     TEXT NOT NULL,
            finished_utc    TEXT,
            status          TEXT NOT NULL,
            contacts_created INTEGER NOT NULL DEFAULT 0,
            contacts_updated INTEGER NOT NULL DEFAULT 0,
            contacts_skipped INTEGER NOT NULL DEFAULT 0,
            notes           TEXT,
            FOREIGN KEY (source_id) REFERENCES sources(id) ON DELETE CASCADE
        );

        CREATE TABLE contacts (
            id              TEXT PRIMARY KEY,
            source_id       TEXT,
            import_id       TEXT,
            uid             TEXT,
            rev             TEXT,
            formatted_name  TEXT,
            given_name      TEXT,
            family_name     TEXT,
            additional_names TEXT,
            honorific_prefix TEXT,
            honorific_suffix TEXT,
            nickname        TEXT,
            organization    TEXT,
            title           TEXT,
            birthday        TEXT,
            anniversary     TEXT,
            notes           TEXT,
            photo_bytes     BLOB,
            photo_mime      TEXT,
            source_file     TEXT,
            source_format   TEXT,
            imported_utc    TEXT NOT NULL,
            updated_utc     TEXT NOT NULL,
            deleted_utc     TEXT,
            custom_fields_json TEXT,
            FOREIGN KEY (source_id) REFERENCES sources(id) ON DELETE SET NULL,
            FOREIGN KEY (import_id) REFERENCES imports(id) ON DELETE SET NULL
        );

        CREATE INDEX ix_contacts_uid       ON contacts(uid);
        CREATE INDEX ix_contacts_source    ON contacts(source_id);
        CREATE INDEX ix_contacts_import    ON contacts(import_id);
        CREATE INDEX ix_contacts_deleted   ON contacts(deleted_utc);

        CREATE TABLE phones (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            contact_id   TEXT NOT NULL,
            raw          TEXT NOT NULL,
            digits       TEXT NOT NULL,
            e164         TEXT,
            kind         TEXT NOT NULL,
            is_preferred INTEGER NOT NULL DEFAULT 0,
            source_id    TEXT,
            FOREIGN KEY (contact_id) REFERENCES contacts(id) ON DELETE CASCADE
        );
        CREATE INDEX ix_phones_contact ON phones(contact_id);
        CREATE INDEX ix_phones_e164    ON phones(e164);
        CREATE INDEX ix_phones_digits  ON phones(digits);

        CREATE TABLE emails (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            contact_id   TEXT NOT NULL,
            address      TEXT NOT NULL,
            canonical    TEXT NOT NULL,
            kind         TEXT NOT NULL,
            is_preferred INTEGER NOT NULL DEFAULT 0,
            source_id    TEXT,
            FOREIGN KEY (contact_id) REFERENCES contacts(id) ON DELETE CASCADE
        );
        CREATE INDEX ix_emails_contact   ON emails(contact_id);
        CREATE INDEX ix_emails_canonical ON emails(canonical);

        CREATE TABLE addresses (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            contact_id   TEXT NOT NULL,
            po_box       TEXT,
            extended     TEXT,
            street       TEXT,
            locality     TEXT,
            region       TEXT,
            postal_code  TEXT,
            country      TEXT,
            kind         TEXT NOT NULL,
            source_id    TEXT,
            FOREIGN KEY (contact_id) REFERENCES contacts(id) ON DELETE CASCADE
        );
        CREATE INDEX ix_addresses_contact ON addresses(contact_id);

        CREATE TABLE urls (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            contact_id TEXT NOT NULL,
            value      TEXT NOT NULL,
            source_id  TEXT,
            FOREIGN KEY (contact_id) REFERENCES contacts(id) ON DELETE CASCADE
        );
        CREATE INDEX ix_urls_contact ON urls(contact_id);

        CREATE TABLE categories (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            contact_id TEXT NOT NULL,
            value      TEXT NOT NULL,
            source_id  TEXT,
            FOREIGN KEY (contact_id) REFERENCES contacts(id) ON DELETE CASCADE
        );
        CREATE INDEX ix_categories_contact ON categories(contact_id);
        CREATE INDEX ix_categories_value   ON categories(value);

        CREATE TABLE audit_log (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            ts_utc      TEXT    NOT NULL,
            op          TEXT    NOT NULL,
            contact_id  TEXT,
            payload     TEXT
        );
        CREATE INDEX ix_audit_ts ON audit_log(ts_utc);

        CREATE TABLE undo_journal (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            ts_utc      TEXT    NOT NULL,
            op          TEXT    NOT NULL,
            label       TEXT,
            forward     TEXT    NOT NULL,
            inverse     TEXT    NOT NULL,
            applied     INTEGER NOT NULL DEFAULT 1
        );
        CREATE INDEX ix_undo_ts ON undo_journal(ts_utc);

        CREATE TABLE rollback_snapshots (
            id           TEXT PRIMARY KEY,
            import_id    TEXT NOT NULL,
            created_utc  TEXT NOT NULL,
            label        TEXT,
            blob_json    TEXT NOT NULL,
            FOREIGN KEY (import_id) REFERENCES imports(id) ON DELETE CASCADE
        );
        """;
}
