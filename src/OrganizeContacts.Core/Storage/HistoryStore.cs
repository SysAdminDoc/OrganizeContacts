using Microsoft.Data.Sqlite;

namespace OrganizeContacts.Core.Storage;

public sealed class HistoryStore : IDisposable
{
    private readonly SqliteConnection _conn;

    public HistoryStore(string dbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS audit_log (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                ts_utc      TEXT    NOT NULL,
                op          TEXT    NOT NULL,
                contact_id  TEXT,
                payload     TEXT
            );

            CREATE TABLE IF NOT EXISTS undo_journal (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                ts_utc      TEXT    NOT NULL,
                op          TEXT    NOT NULL,
                forward     TEXT    NOT NULL,
                inverse     TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_audit_ts ON audit_log(ts_utc);
            CREATE INDEX IF NOT EXISTS ix_undo_ts  ON undo_journal(ts_utc);
            """;
        cmd.ExecuteNonQuery();
    }

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
