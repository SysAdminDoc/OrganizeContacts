using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace OrganizeContacts.Core.Storage;

public sealed record UndoEntry(
    long Id,
    DateTimeOffset Timestamp,
    string Op,
    string? Label,
    string ForwardJson,
    string InverseJson,
    bool Applied);

public sealed class HistoryStore
{
    private readonly ContactRepository _repo;

    public HistoryStore(ContactRepository repo) => _repo = repo;

    public void Audit(string op, Guid? contactId = null, string? payload = null)
        => _repo.Audit(op, contactId, payload);

    public long RecordUndo(string op, object forward, object inverse, string? label = null)
    {
        // Two-step on purpose: relying on multi-statement ExecuteScalar to surface
        // last_insert_rowid() is fragile (the provider only guarantees the first
        // result-producing statement; it works today but isn't documented).
        using (var ins = _repo.Connection.CreateCommand())
        {
            ins.CommandText = """
                INSERT INTO undo_journal (ts_utc, op, label, forward, inverse, applied)
                VALUES ($ts, $op, $label, $fwd, $inv, 1);
                """;
            ins.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
            ins.Parameters.AddWithValue("$op", op);
            ins.Parameters.AddWithValue("$label", (object?)label ?? DBNull.Value);
            ins.Parameters.AddWithValue("$fwd", JsonSerializer.Serialize(forward));
            ins.Parameters.AddWithValue("$inv", JsonSerializer.Serialize(inverse));
            ins.ExecuteNonQuery();
        }
        using var pick = _repo.Connection.CreateCommand();
        pick.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(pick.ExecuteScalar());
    }

    public IReadOnlyList<UndoEntry> ListUndo(int limit = 200)
    {
        var list = new List<UndoEntry>();
        using var cmd = _repo.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, ts_utc, op, label, forward, inverse, applied
            FROM undo_journal ORDER BY id DESC LIMIT $lim;
            """;
        cmd.Parameters.AddWithValue("$lim", limit);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            list.Add(new UndoEntry(
                rdr.GetInt64(0),
                DateTimeOffset.Parse(rdr.GetString(1), System.Globalization.CultureInfo.InvariantCulture),
                rdr.GetString(2),
                rdr.IsDBNull(3) ? null : rdr.GetString(3),
                rdr.GetString(4),
                rdr.GetString(5),
                rdr.GetInt32(6) != 0));
        }
        return list;
    }

    public UndoEntry? GetMostRecentApplied()
    {
        using var cmd = _repo.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, ts_utc, op, label, forward, inverse, applied
            FROM undo_journal WHERE applied = 1 ORDER BY id DESC LIMIT 1;
            """;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;
        return new UndoEntry(
            rdr.GetInt64(0),
            DateTimeOffset.Parse(rdr.GetString(1), System.Globalization.CultureInfo.InvariantCulture),
            rdr.GetString(2),
            rdr.IsDBNull(3) ? null : rdr.GetString(3),
            rdr.GetString(4),
            rdr.GetString(5),
            rdr.GetInt32(6) != 0);
    }

    public void MarkUndone(long id)
    {
        using var cmd = _repo.Connection.CreateCommand();
        cmd.CommandText = "UPDATE undo_journal SET applied = 0 WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
