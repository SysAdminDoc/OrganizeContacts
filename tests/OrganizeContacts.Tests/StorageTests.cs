using System.IO;
using Microsoft.Data.Sqlite;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Storage;

namespace OrganizeContacts.Tests;

public class StorageTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ContactRepository _repo;

    public StorageTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"oc-tests-{Guid.NewGuid():N}.sqlite");
        _repo = new ContactRepository(_dbPath);
    }

    public void Dispose()
    {
        _repo.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public void Source_kind_values_remain_stable_for_persisted_databases()
    {
        Assert.Equal(7, (int)SourceKind.Thunderbird);
        Assert.Equal(8, (int)SourceKind.Manual);
        Assert.Equal(9, (int)SourceKind.AndroidDatabase);
    }

    [Fact]
    public void Version_one_database_migrates_address_preference_column()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-v1-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var create = connection.CreateCommand();
                create.CommandText = """
                    CREATE TABLE schema_version (version INTEGER PRIMARY KEY, applied_utc TEXT NOT NULL);
                    INSERT INTO schema_version(version, applied_utc) VALUES (1, '2026-01-01T00:00:00Z');
                    CREATE TABLE addresses (id INTEGER PRIMARY KEY);
                    """;
                create.ExecuteNonQuery();
            }

            using var migrated = new ContactRepository(path);
            using var columns = migrated.Connection.CreateCommand();
            columns.CommandText = "SELECT name FROM pragma_table_info('addresses') WHERE name = 'is_preferred';";
            Assert.Equal("is_preferred", columns.ExecuteScalar());

            using var version = migrated.Connection.CreateCommand();
            version.CommandText = "SELECT MAX(version) FROM schema_version;";
            Assert.Equal(2L, version.ExecuteScalar());
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + suffix); } catch { }
        }
    }

    [Fact]
    public void Round_trips_contact_with_children()
    {
        var src = _repo.UpsertSource(new ContactSource { Kind = SourceKind.File, Label = "test" });
        var c = new Contact
        {
            FormattedName = "Round Trip",
            GivenName = "Round",
            FamilyName = "Trip",
            SourceId = src.Id,
        };
        c.Phones.Add(PhoneNumber.Parse("5551234567", PhoneKind.Mobile));
        c.Emails.Add(new EmailAddress { Address = "rt@example.com" });
        c.Addresses.Add(new PostalAddress
        {
            Street = "123 Main Street",
            Kind = AddressKind.Work,
            IsPreferred = true,
        });
        c.Categories.Add("Friends");
        c.CustomFields["X-FOO"] = "bar";

        _repo.InsertContact(c);

        var read = _repo.GetById(c.Id);
        Assert.NotNull(read);
        Assert.Equal("Round Trip", read!.FormattedName);
        Assert.Single(read.Phones);
        Assert.Single(read.Emails);
        Assert.True(Assert.Single(read.Addresses).IsPreferred);
        Assert.Single(read.Categories);
        Assert.Equal("bar", read.CustomFields["X-FOO"]);
    }

    [Fact]
    public void Find_by_uid_respects_source_filter()
    {
        var src = _repo.UpsertSource(new ContactSource { Kind = SourceKind.File, Label = "test" });
        var c = new Contact { Uid = "urn:uuid:1", FormattedName = "A", SourceId = src.Id };
        _repo.InsertContact(c);

        var found = _repo.FindByUid("urn:uuid:1", src.Id);
        Assert.NotNull(found);

        var miss = _repo.FindByUid("urn:uuid:2");
        Assert.Null(miss);
    }

    [Fact]
    public void Soft_delete_hides_from_listing_but_restore_works()
    {
        var c = new Contact { FormattedName = "deleteme" };
        _repo.InsertContact(c);
        Assert.NotNull(_repo.GetById(c.Id));

        _repo.SoftDeleteContact(c.Id);
        Assert.Null(_repo.GetById(c.Id));
        Assert.DoesNotContain(_repo.ListContacts(), x => x.Id == c.Id);

        _repo.RestoreContact(c.Id);
        Assert.NotNull(_repo.GetById(c.Id));
    }

    [Fact]
    public void Tool_operation_snapshot_restores_soft_deleted_contacts()
    {
        var source = _repo.UpsertSource(new ContactSource
        {
            Kind = SourceKind.Manual,
            Label = "tools",
            FilePath = "organizecontacts://tools",
        });
        var contact = new Contact
        {
            FormattedName = "Before Cleanup",
            SourceId = source.Id,
        };
        contact.Emails.Add(new EmailAddress { Address = "before@example.com" });
        _repo.InsertContact(contact);

        var op = _repo.StartImport(new ImportRecord
        {
            SourceId = source.Id,
            FilePath = "tool:cleanup",
            Status = ImportStatus.Pending,
        });
        var rollback = new RollbackService(_repo);
        var snapshotId = rollback.CaptureForImport(op.Id, new[] { contact }, "before cleanup");

        contact.FormattedName = "After Cleanup";
        contact.Emails[0] = new EmailAddress { Address = "after@example.com" };
        _repo.UpdateContact(contact);
        _repo.SoftDeleteContact(contact.Id);
        op.FinishedAt = DateTimeOffset.UtcNow;
        op.Status = ImportStatus.Committed;
        op.ContactsUpdated = 1;
        _repo.FinishImport(op);

        Assert.Null(_repo.GetById(contact.Id));

        Assert.True(rollback.Restore(snapshotId));
        var restored = _repo.GetById(contact.Id);
        Assert.NotNull(restored);
        Assert.Equal("Before Cleanup", restored!.FormattedName);
        Assert.Equal("before@example.com", Assert.Single(restored.Emails).Address);
    }
}
