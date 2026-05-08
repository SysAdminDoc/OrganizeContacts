using System.IO;
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
        c.Categories.Add("Friends");
        c.CustomFields["X-FOO"] = "bar";

        _repo.InsertContact(c);

        var read = _repo.GetById(c.Id);
        Assert.NotNull(read);
        Assert.Equal("Round Trip", read!.FormattedName);
        Assert.Single(read.Phones);
        Assert.Single(read.Emails);
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
}
