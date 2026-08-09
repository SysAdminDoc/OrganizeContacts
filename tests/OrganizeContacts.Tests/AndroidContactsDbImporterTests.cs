using Microsoft.Data.Sqlite;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Tests;

public sealed class AndroidContactsDbImporterTests
{
    [Fact]
    public async Task Reads_standard_contacts_provider_data_rows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-android-{Guid.NewGuid():N}.db");
        try
        {
            CreateDatabase(path);
            var importer = new AndroidContactsDbImporter();
            Assert.True(importer.CanRead(path));

            var contacts = new List<Contact>();
            await foreach (var contact in importer.ReadAsync(path)) contacts.Add(contact);

            var imported = Assert.Single(contacts);
            Assert.Equal("Ada Lovelace", imported.FormattedName);
            Assert.Equal("Ada", imported.GivenName);
            Assert.Equal("Lovelace", imported.FamilyName);
            Assert.Equal("Countess", imported.HonorificPrefix);
            Assert.Equal("Augusta", imported.AdditionalNames);
            Assert.Equal("Acme", imported.Organization);
            Assert.Equal("Engineer", imported.Title);
            Assert.Equal("+15551234567", Assert.Single(imported.Phones).Raw);
            Assert.Equal(PhoneKind.Mobile, imported.Phones[0].Kind);
            Assert.Equal("ada@example.com", Assert.Single(imported.Emails).Address);
            Assert.Equal(EmailKind.Work, imported.Emails[0].Kind);
            Assert.Equal("1 Main Street", Assert.Single(imported.Addresses).Street);
            Assert.Equal("London", imported.Addresses[0].Locality);
            Assert.Equal("Remember this.", imported.Notes);
            Assert.Contains("https://example.com/ada", imported.Urls);
            Assert.Contains("Friends", imported.Categories);
            Assert.Equal(new DateOnly(1815, 12, 10), imported.Birthday);
            Assert.Equal(new DateOnly(1843, 7, 8), imported.Anniversary);
            Assert.Equal("image/jpeg", imported.PhotoMimeType);
            Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, imported.PhotoBytes);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static void CreateDatabase(string path)
    {
        NextId = 1;
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        Execute(connection, """
            CREATE TABLE raw_contacts (_id INTEGER PRIMARY KEY);
            CREATE TABLE mimetypes (_id INTEGER PRIMARY KEY, mimetype TEXT NOT NULL);
            CREATE TABLE groups (_id INTEGER PRIMARY KEY, title TEXT);
            CREATE TABLE data (
                _id INTEGER PRIMARY KEY,
                raw_contact_id INTEGER NOT NULL,
                mimetype_id INTEGER NOT NULL,
                data1 TEXT, data2 TEXT, data3 TEXT, data4 TEXT, data5 TEXT,
                data6 TEXT, data7 TEXT, data8 TEXT, data9 TEXT, data10 TEXT,
                data11 TEXT, data12 TEXT, data13 TEXT, data14 TEXT, data15 BLOB
            );
            INSERT INTO raw_contacts (_id) VALUES (1);
            INSERT INTO groups (_id, title) VALUES (5, 'Friends');
            """);

        var mimetypes = new[]
        {
            (1, "vnd.android.cursor.item/name"),
            (2, "vnd.android.cursor.item/phone_v2"),
            (3, "vnd.android.cursor.item/email_v2"),
            (4, "vnd.android.cursor.item/organization"),
            (5, "vnd.android.cursor.item/postal-address_v2"),
            (6, "vnd.android.cursor.item/note"),
            (7, "vnd.android.cursor.item/website"),
            (8, "vnd.android.cursor.item/contact_event"),
            (9, "vnd.android.cursor.item/group_membership"),
            (10, "vnd.android.cursor.item/photo"),
        };
        foreach (var (id, mime) in mimetypes)
            Execute(connection, $"INSERT INTO mimetypes (_id, mimetype) VALUES ({id}, '{mime}');");

        InsertData(connection, 1, "Ada Lovelace", "Ada", "Lovelace", "Countess", "Augusta");
        InsertData(connection, 2, "+15551234567", "2");
        InsertData(connection, 3, "ada@example.com", "2");
        InsertData(connection, 4, "Acme", null, null, "Engineer");
        InsertData(connection, 5, "1 Main Street, London", "1", null, "1 Main Street", null, null, "London", "London", "SW1A 1AA", "UK");
        InsertData(connection, 6, "Remember this.");
        InsertData(connection, 7, "https://example.com/ada");
        InsertData(connection, 8, "1815-12-10", "3");
        InsertData(connection, 8, "1843-07-08", "1");
        InsertData(connection, 9, "5");
        InsertData(connection, 10, photo: new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
    }

    private static void InsertData(
        SqliteConnection connection,
        int mimetypeId,
        string? data1 = null,
        string? data2 = null,
        string? data3 = null,
        string? data4 = null,
        string? data5 = null,
        string? data6 = null,
        string? data7 = null,
        string? data8 = null,
        string? data9 = null,
        string? data10 = null,
        byte[]? photo = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO data (
                _id, raw_contact_id, mimetype_id,
                data1, data2, data3, data4, data5, data6, data7, data8, data9, data10, data15)
            VALUES ($id, 1, $mime, $d1, $d2, $d3, $d4, $d5, $d6, $d7, $d8, $d9, $d10, $photo);
            """;
        command.Parameters.AddWithValue("$id", NextId++);
        command.Parameters.AddWithValue("$mime", mimetypeId);
        var values = new[] { data1, data2, data3, data4, data5, data6, data7, data8, data9, data10 };
        for (var i = 0; i < values.Length; i++)
            command.Parameters.AddWithValue($"$d{i + 1}", (object?)values[i] ?? DBNull.Value);
        command.Parameters.AddWithValue("$photo", (object?)photo ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static long NextId { get; set; } = 1;

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
