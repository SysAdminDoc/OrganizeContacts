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
        using var cmd = _repo.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO undo_journal (ts_utc, op, label, forward, inverse, applied)
            VALUES ($ts, $op, $label, $fwd, $inv, 1);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$op", op);
        cmd.Parameters.AddWithValue("$label", (object?)label ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fwd", JsonSerializer.Serialize(forward));
        cmd.Parameters.AddWithValue("$inv", JsonSerializer.Serialize(inverse));
        return Convert.ToInt64(cmd.ExecuteScalar());
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
