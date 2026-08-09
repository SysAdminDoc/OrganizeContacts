using System.Globalization;
using Microsoft.Data.Sqlite;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

/// <summary>
/// Imports the local SQLite database used by Android ContactsProvider exports.
/// The reader is intentionally read-only and works from raw_contacts/data/mimetypes
/// rows so it does not depend on a particular vendor's aggregate-contact view.
/// </summary>
public sealed class AndroidContactsDbImporter : IContactImporter
{
    private const string NameMime = "vnd.android.cursor.item/name";
    private const string PhoneMime = "vnd.android.cursor.item/phone_v2";
    private const string EmailMime = "vnd.android.cursor.item/email_v2";
    private const string OrganizationMime = "vnd.android.cursor.item/organization";
    private const string PostalMime = "vnd.android.cursor.item/postal-address_v2";
    private const string NoteMime = "vnd.android.cursor.item/note";
    private const string WebsiteMime = "vnd.android.cursor.item/website";
    private const string EventMime = "vnd.android.cursor.item/contact_event";
    private const string GroupMime = "vnd.android.cursor.item/group_membership";
    private const string PhotoMime = "vnd.android.cursor.item/photo";

    public string Name => "Android contacts2.db";
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".db", ".sqlite", ".sqlite3" };

    public bool CanRead(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('raw_contacts', 'data', 'mimetypes');
                """;
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 3;
        }
        catch
        {
            return false;
        }
    }

    public async IAsyncEnumerable<Contact> ReadAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        using var connection = Open(path);
        var contacts = LoadRawContacts(connection, path);
        var groups = LoadGroups(connection);
        LoadData(connection, contacts, groups);

        foreach (var contact in contacts.OrderBy(x => x.Key).Select(x => x.Value))
        {
            ct.ThrowIfCancellationRequested();
            if (HasPersonData(contact)) yield return contact;
        }
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static Dictionary<long, Contact> LoadRawContacts(SqliteConnection connection, string path)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT _id FROM raw_contacts ORDER BY _id;";
        using var reader = command.ExecuteReader();
        var contacts = new Dictionary<long, Contact>();
        while (reader.Read())
        {
            var rawId = reader.GetInt64(0);
            contacts[rawId] = new Contact
            {
                Uid = $"android:raw-contact:{rawId}",
                SourceFile = path,
                SourceFormat = "Android contacts2.db",
            };
        }
        return contacts;
    }

    private static Dictionary<long, string> LoadGroups(SqliteConnection connection)
    {
        var groups = new Dictionary<long, string>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT _id, title FROM groups;";
        try
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var title = Text(reader, 1);
                if (!string.IsNullOrWhiteSpace(title)) groups[reader.GetInt64(0)] = title!;
            }
        }
        catch (SqliteException)
        {
            // Some exports omit the optional groups table.
        }
        return groups;
    }

    private static void LoadData(
        SqliteConnection connection,
        Dictionary<long, Contact> contacts,
        IReadOnlyDictionary<long, string> groups)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d._id, d.raw_contact_id, m.mimetype,
                   d.data1, d.data2, d.data3, d.data4, d.data5,
                   d.data6, d.data7, d.data8, d.data9, d.data10,
                   d.data11, d.data12, d.data13, d.data14, d.data15
            FROM data d
            JOIN mimetypes m ON m._id = d.mimetype_id
            ORDER BY d.raw_contact_id, d._id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var rawId = reader.GetInt64(1);
            if (!contacts.TryGetValue(rawId, out var contact)) continue;

            var mime = Text(reader, 2);
            if (string.IsNullOrWhiteSpace(mime)) continue;
            var data = new string?[15];
            for (var i = 0; i < data.Length; i++) data[i] = Text(reader, i + 3);
            var blob = Blob(reader, 17);
            ApplyData(contact, mime!, data, blob, groups);
        }
    }

    private static void ApplyData(
        Contact contact,
        string mime,
        string?[] data,
        byte[]? blob,
        IReadOnlyDictionary<long, string> groups)
    {
        switch (mime)
        {
            case NameMime:
                contact.FormattedName ??= NullIfEmpty(data[0]);
                contact.GivenName ??= NullIfEmpty(data[1]);
                contact.FamilyName ??= NullIfEmpty(data[2]);
                contact.HonorificPrefix ??= NullIfEmpty(data[3]);
                contact.AdditionalNames ??= NullIfEmpty(data[4]);
                contact.HonorificSuffix ??= NullIfEmpty(data[5]);
                break;

            case PhoneMime:
                AddPhone(contact, data[0], ParsePhoneKind(data[1]));
                break;

            case EmailMime:
                AddEmail(contact, data[0], ParseEmailKind(data[1]));
                break;

            case OrganizationMime:
                contact.Organization ??= NullIfEmpty(data[0]);
                contact.Title ??= NullIfEmpty(data[3]);
                break;

            case PostalMime:
                var address = new PostalAddress
                {
                    Street = NullIfEmpty(data[3]) ?? NullIfEmpty(data[0]),
                    PoBox = NullIfEmpty(data[4]),
                    Locality = NullIfEmpty(data[6]),
                    Region = NullIfEmpty(data[7]),
                    PostalCode = NullIfEmpty(data[8]),
                    Country = NullIfEmpty(data[9]),
                    Kind = ParseAddressKind(data[1]),
                };
                if (!string.IsNullOrWhiteSpace(address.OneLine)) contact.Addresses.Add(address);
                break;

            case NoteMime:
                AppendNote(contact, data[0]);
                break;

            case WebsiteMime:
                if (!string.IsNullOrWhiteSpace(data[0])) contact.Urls.Add(data[0]!);
                break;

            case EventMime:
                ApplyEvent(contact, data[0], data[1]);
                break;

            case GroupMime:
                if (long.TryParse(data[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var groupId) &&
                    groups.TryGetValue(groupId, out var groupName))
                    contact.Categories.Add(groupName);
                break;

            case PhotoMime:
                if (blob is { Length: > 0 })
                {
                    contact.PhotoBytes ??= blob;
                    contact.PhotoMimeType ??= InferPhotoMime(blob);
                }
                break;

            default:
                var customValue = string.Join("|", data.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (!string.IsNullOrWhiteSpace(customValue))
                {
                    var key = "X-ANDROID-" + new string(mime
                        .Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '-')
                        .ToArray()).Trim('-');
                    contact.CustomFields[key] = customValue;
                }
                break;
        }
    }

    private static void AddPhone(Contact contact, string? raw, PhoneKind kind)
    {
        if (!string.IsNullOrWhiteSpace(raw)) contact.Phones.Add(PhoneNumber.Parse(raw!, kind));
    }

    private static void AddEmail(Contact contact, string? address, EmailKind kind)
    {
        if (!string.IsNullOrWhiteSpace(address))
            contact.Emails.Add(new EmailAddress { Address = address!, Kind = kind });
    }

    private static void AppendNote(Contact contact, string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return;
        contact.Notes = string.IsNullOrWhiteSpace(contact.Notes)
            ? note
            : contact.Notes + Environment.NewLine + note;
    }

    private static void ApplyEvent(Contact contact, string? value, string? type)
    {
        if (!TryParseDate(value, out var date)) return;
        if (int.TryParse(type, NumberStyles.Integer, CultureInfo.InvariantCulture, out var eventType))
        {
            if (eventType == 3) contact.Birthday ??= date;
            else if (eventType == 1) contact.Anniversary ??= date;
        }
    }

    private static bool HasPersonData(Contact contact) =>
        !string.IsNullOrWhiteSpace(contact.DisplayName) ||
        !string.IsNullOrWhiteSpace(contact.Organization) ||
        contact.Phones.Count > 0 ||
        contact.Emails.Count > 0 ||
        contact.Addresses.Count > 0 ||
        contact.Urls.Count > 0 ||
        !string.IsNullOrWhiteSpace(contact.Notes) ||
        contact.PhotoBytes is { Length: > 0 } ||
        contact.Categories.Count > 0 ||
        contact.CustomFields.Count > 0;

    private static PhoneKind ParsePhoneKind(string? value) => value switch
    {
        "1" => PhoneKind.Home,
        "2" => PhoneKind.Mobile,
        "3" => PhoneKind.Work,
        "4" or "5" => PhoneKind.Fax,
        "6" => PhoneKind.Pager,
        "10" => PhoneKind.Main,
        "12" => PhoneKind.Main,
        _ => PhoneKind.Other,
    };

    private static EmailKind ParseEmailKind(string? value) => value switch
    {
        "1" => EmailKind.Personal,
        "2" => EmailKind.Work,
        _ => EmailKind.Other,
    };

    private static AddressKind ParseAddressKind(string? value) => value switch
    {
        "1" => AddressKind.Home,
        "2" => AddressKind.Work,
        _ => AddressKind.Other,
    };

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        var formats = new[] { "yyyy-MM-dd", "yyyyMMdd", "MM/dd/yyyy" };
        return DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);
    }

    private static string? InferPhotoMime(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return "image/gif";
        return null;
    }

    private static string? Text(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture);

    private static byte[]? Blob(SqliteDataReader reader, int index)
    {
        if (reader.IsDBNull(index)) return null;
        var value = reader.GetValue(index);
        return value switch
        {
            byte[] bytes => bytes,
            _ => null,
        };
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
