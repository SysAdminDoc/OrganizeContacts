using System.IO;
using System.Text;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

/// <summary>
/// LDIF v1 (RFC 2849) importer for Thunderbird/Mozilla MAB exports.
/// Maps Mozilla address book attributes (cn, mail, telephoneNumber, mozillaSecondEmail,
/// homePhone, workPhone, cellPhone, etc.) into Contact fields. base64 attribute values
/// (`attr::`) are decoded.
/// </summary>
public sealed class LdifImporter : IContactImporter
{
    public string Name => "LDIF (Thunderbird/Mozilla)";
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".ldif" };

    public bool CanRead(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public async IAsyncEnumerable<Contact> ReadAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Read all raw lines, then unfold continuation lines (LDIF v1: leading space).
        var raw = new List<string>();
        using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null) raw.Add(line);
        }

        var unfolded = new List<string>(raw.Count);
        foreach (var ln in raw)
        {
            if (ln.Length > 0 && (ln[0] == ' ' || ln[0] == '\t') && unfolded.Count > 0)
                unfolded[^1] += ln[1..];
            else
                unfolded.Add(ln);
        }

        var attrs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in unfolded)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                if (attrs.Count > 0)
                {
                    var c = MapAttrs(attrs, path);
                    if (c is not null) yield return c;
                    attrs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                }
                continue;
            }
            if (line.StartsWith("#")) continue;

            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var attr = line[..idx];
            var rest = line[(idx + 1)..];
            string value;
            if (rest.StartsWith(":"))
            {
                try { value = Encoding.UTF8.GetString(Convert.FromBase64String(rest[1..].Trim())); }
                catch { continue; }
            }
            else
            {
                value = rest.TrimStart();
            }
            if (!attrs.TryGetValue(attr, out var list))
                attrs[attr] = list = new List<string>();
            list.Add(value);
        }

        if (attrs.Count > 0)
        {
            var c = MapAttrs(attrs, path);
            if (c is not null) yield return c;
        }
    }

    private static Contact? MapAttrs(Dictionary<string, List<string>> attrs, string source)
    {
        // Skip group/dn-only blocks. Accept any "person-shaped" attribute as proof there's
        // real data here — the previous gate only allowed cn/givenName/mail and silently
        // dropped phone-only or address-only Mozilla MAB exports.
        if (!attrs.ContainsKey("cn") && !attrs.ContainsKey("givenName") &&
            !attrs.ContainsKey("sn") && !attrs.ContainsKey("mail") &&
            !attrs.ContainsKey("mozillaSecondEmail") &&
            !attrs.ContainsKey("telephoneNumber") && !attrs.ContainsKey("homePhone") &&
            !attrs.ContainsKey("workPhone") && !attrs.ContainsKey("cellPhone") &&
            !attrs.ContainsKey("mobile") && !attrs.ContainsKey("facsimileTelephoneNumber") &&
            !attrs.ContainsKey("pager") &&
            !attrs.ContainsKey("street") && !attrs.ContainsKey("mozillaWorkStreet"))
            return null;

        var c = new Contact { SourceFile = source, SourceFormat = "LDIF" };
        var seen = false;

        if (attrs.TryGetValue("cn", out var cn) && cn.Count > 0)        { c.FormattedName = cn[0]; seen = true; }
        if (attrs.TryGetValue("givenName", out var gn) && gn.Count > 0)  { c.GivenName = gn[0]; seen = true; }
        if (attrs.TryGetValue("sn", out var sn) && sn.Count > 0)         { c.FamilyName = sn[0]; seen = true; }
        if (attrs.TryGetValue("mozillaNickname", out var nick) && nick.Count > 0) { c.Nickname = nick[0]; seen = true; }
        if (attrs.TryGetValue("o", out var org) && org.Count > 0)        { c.Organization = org[0]; seen = true; }
        if (attrs.TryGetValue("title", out var title) && title.Count > 0) c.Title = title[0];
        if (attrs.TryGetValue("description", out var notes) && notes.Count > 0) c.Notes = notes[0];

        AddEmails("mail", EmailKind.Other);
        AddEmails("mozillaSecondEmail", EmailKind.Other);
        AddPhone("telephoneNumber", PhoneKind.Other);
        AddPhone("homePhone", PhoneKind.Home);
        AddPhone("workPhone", PhoneKind.Work);
        AddPhone("cellPhone", PhoneKind.Mobile);
        AddPhone("mobile", PhoneKind.Mobile);
        AddPhone("facsimileTelephoneNumber", PhoneKind.Fax);
        AddPhone("pager", PhoneKind.Pager);

        AddAddress("street", "l", "st", "postalCode", "c", AddressKind.Home);
        AddAddress("mozillaWorkStreet", "mozillaWorkCity", "mozillaWorkState",
                   "mozillaWorkPostalCode", "mozillaWorkCountry", AddressKind.Work);

        // A card with only mail / phone / address is still a card — pre-fix `seen`
        // was set only on cn/givenName/sn so e.g. a Thunderbird "mail-only" entry was
        // silently dropped. Now any populated child collection counts as content.
        if (c.Emails.Count > 0 || c.Phones.Count > 0 || c.Addresses.Count > 0) seen = true;

        return seen ? c : null;

        void AddEmails(string key, EmailKind kind)
        {
            if (!attrs.TryGetValue(key, out var list)) return;
            foreach (var v in list)
                if (!string.IsNullOrWhiteSpace(v)) c.Emails.Add(new EmailAddress { Address = v.Trim(), Kind = kind });
        }
        void AddPhone(string key, PhoneKind kind)
        {
            if (!attrs.TryGetValue(key, out var list)) return;
            foreach (var v in list)
                if (!string.IsNullOrWhiteSpace(v)) c.Phones.Add(PhoneNumber.Parse(v.Trim(), kind));
        }
        void AddAddress(string st, string l, string region, string pc, string country, AddressKind kind)
        {
            attrs.TryGetValue(st, out var s);
            attrs.TryGetValue(l, out var lo);
            attrs.TryGetValue(region, out var r);
            attrs.TryGetValue(pc, out var p);
            attrs.TryGetValue(country, out var co);
            if ((s?.Count ?? 0) + (lo?.Count ?? 0) + (r?.Count ?? 0) + (p?.Count ?? 0) + (co?.Count ?? 0) == 0) return;
            c.Addresses.Add(new PostalAddress
            {
                Street = s?.FirstOrDefault(),
                Locality = lo?.FirstOrDefault(),
                Region = r?.FirstOrDefault(),
                PostalCode = p?.FirstOrDefault(),
                Country = co?.FirstOrDefault(),
                Kind = kind,
            });
        }
    }
}
