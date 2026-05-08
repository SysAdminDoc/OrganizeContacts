using System.Text.Json;
using Microsoft.Data.Sqlite;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Storage;

public sealed record RollbackSnapshot(Guid Id, Guid ImportId, DateTimeOffset CreatedAt, string Label);

/// <summary>
/// Captures a before-state snapshot for an import (existing contacts that may be touched)
/// so a Restore action can revert. Snapshot payload is JSON for portability and inspectability.
/// </summary>
public sealed class RollbackService
{
    private readonly ContactRepository _repo;
    public RollbackService(ContactRepository repo) => _repo = repo;

    public Guid CaptureForImport(Guid importId, IEnumerable<Contact> before, string label = "")
    {
        var id = Guid.NewGuid();
        var json = JsonSerializer.Serialize(before, new JsonSerializerOptions { WriteIndented = false });
        using var cmd = _repo.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rollback_snapshots (id, import_id, created_utc, label, blob_json)
            VALUES ($id, $imp, $ts, $label, $blob);
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$imp", importId.ToString());
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$label", label ?? string.Empty);
        cmd.Parameters.AddWithValue("$blob", json);
        cmd.ExecuteNonQuery();
        return id;
    }

    public IReadOnlyList<RollbackSnapshot> List(int limit = 50)
    {
        var list = new List<RollbackSnapshot>();
        using var cmd = _repo.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, import_id, created_utc, label
            FROM rollback_snapshots ORDER BY created_utc DESC LIMIT $lim;
            """;
        cmd.Parameters.AddWithValue("$lim", limit);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            list.Add(new RollbackSnapshot(
                Guid.Parse(rdr.GetString(0)),
                Guid.Parse(rdr.GetString(1)),
                DateTimeOffset.Parse(rdr.GetString(2), System.Globalization.CultureInfo.InvariantCulture),
                rdr.GetString(3)));
        }
        return list;
    }

    public bool Restore(Guid snapshotId)
    {
        string? json = null;
        Guid importId;
        using (var cmd = _repo.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT import_id, blob_json FROM rollback_snapshots WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", snapshotId.ToString());
            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return false;
            importId = Guid.Parse(rdr.GetString(0));
            json = rdr.GetString(1);
        }

        var snapshot = JsonSerializer.Deserialize<List<Contact>>(json) ?? new List<Contact>();
        var snapshotIds = snapshot.Select(c => c.Id).ToHashSet();

        using var tx = _repo.BeginTransaction();
        // Hard-delete contacts that were created by the rolled-back import.
        using (var cmd = _repo.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id FROM contacts WHERE import_id = $imp;";
            cmd.Parameters.AddWithValue("$imp", importId.ToString());
            var ids = new List<Guid>();
            using (var rdr = cmd.ExecuteReader())
                while (rdr.Read()) ids.Add(Guid.Parse(rdr.GetString(0)));
            foreach (var id in ids)
                if (!snapshotIds.Contains(id))
                    _repo.HardDeleteContact(id, tx);
        }

        // Restore the prior state of contacts that existed before the import.
        foreach (var c in snapshot)
        {
            var still = _repo.GetById(c.Id);
            if (still is null) _repo.InsertContact(c, tx);
            else _repo.UpdateContact(c, tx);
        }

        using (var cmd = _repo.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE imports SET status = 'RolledBack' WHERE id = $imp;";
            cmd.Parameters.AddWithValue("$imp", importId.ToString());
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        _repo.Audit("rollback.restore", payload: $"snapshot={snapshotId};import={importId}");
        return true;
    }
}
