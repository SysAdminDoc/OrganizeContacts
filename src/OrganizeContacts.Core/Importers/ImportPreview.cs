using System.Globalization;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Normalize;
using OrganizeContacts.Core.Storage;

namespace OrganizeContacts.Core.Importers;

public enum ImportAction
{
    New,
    UpdateNewer,
    SkipUnchanged,
    SkipOlder,
    Conflict,
}

public sealed record ImportPreviewItem(
    Contact Incoming,
    Contact? Existing,
    ImportAction Action,
    string Reason);

public sealed class ImportPreviewReport
{
    public List<ImportPreviewItem> Items { get; } = new();
    public int Total => Items.Count;
    public int New => Items.Count(i => i.Action == ImportAction.New);
    public int Updates => Items.Count(i => i.Action == ImportAction.UpdateNewer);
    public int Skipped => Items.Count(i => i.Action is ImportAction.SkipUnchanged or ImportAction.SkipOlder);
    public int Conflicts => Items.Count(i => i.Action == ImportAction.Conflict);

    public string Summary =>
        $"{Total} cards: +{New} new, ~{Updates} update, {Skipped} skip, {Conflicts} conflict.";
}

/// <summary>
/// Computes a preview for a vCard import without writing anything. Caller decides whether to commit.
/// UID-based: matches existing contacts by UID, then compares REV/UpdatedAt.
/// </summary>
public sealed class ImportPreviewer
{
    private readonly ContactRepository _repo;
    private readonly PhoneNormalizer? _phone;
    private readonly EmailCanonicalizer _email;

    public ImportPreviewer(ContactRepository repo, PhoneNormalizer? phone = null, EmailCanonicalizer? email = null)
    {
        _repo = repo;
        _phone = phone;
        _email = email ?? EmailCanonicalizer.Default;
    }

    public async Task<ImportPreviewReport> PreviewAsync(
        IContactImporter importer,
        string path,
        Guid? sourceId = null,
        CancellationToken ct = default)
    {
        var report = new ImportPreviewReport();

        await foreach (var incoming in importer.ReadAsync(path, ct))
        {
            // Apply normalization so previewed contacts are presented in their final shape.
            _phone?.Normalize(incoming);
            _email.Apply(incoming);

            Contact? existing = null;
            if (!string.IsNullOrWhiteSpace(incoming.Uid))
                existing = _repo.FindByUid(incoming.Uid!, sourceId);

            if (existing is null)
            {
                report.Items.Add(new ImportPreviewItem(incoming, null, ImportAction.New, "no UID match"));
                continue;
            }

            var cmp = CompareRev(incoming.Rev, existing.Rev);
            if (cmp > 0)
                report.Items.Add(new ImportPreviewItem(incoming, existing, ImportAction.UpdateNewer, $"REV {incoming.Rev} > {existing.Rev}"));
            else if (cmp == 0)
                report.Items.Add(new ImportPreviewItem(incoming, existing, ImportAction.SkipUnchanged, "same REV"));
            else
                report.Items.Add(new ImportPreviewItem(incoming, existing, ImportAction.SkipOlder, $"REV {incoming.Rev} < {existing.Rev}"));
        }

        return report;
    }

    /// <summary>
    /// Compare two vCard REV strings. Both ISO-8601 timestamps and date-only forms are
    /// expected, so we try a real DateTime parse first and only fall back to ordinal
    /// string compare when neither side parses (safer than trusting lexicographic
    /// order on mixed formats like "2026-04-01" vs "20260401T120000Z").
    /// </summary>
    internal static int CompareRev(string? a, string? b)
    {
        var aMissing = string.IsNullOrEmpty(a);
        var bMissing = string.IsNullOrEmpty(b);
        if (aMissing && bMissing) return 0;
        if (aMissing) return -1;
        if (bMissing) return 1;

        if (TryParseRev(a!, out var ta) && TryParseRev(b!, out var tb))
            return ta.CompareTo(tb);

        // One or both unparseable — preserve old ordinal behavior so a manually-edited
        // bumped string ("2") is still treated as "newer" than the prior "1".
        return string.Compare(a, b, StringComparison.Ordinal);
    }

    private static bool TryParseRev(string s, out DateTimeOffset value)
    {
        // ISO-8601 with or without separators / Z suffix. AssumeUniversal so naked
        // timestamps don't get re-anchored to local time before being compared.
        var formats = new[]
        {
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            "yyyyMMddTHHmmssZ",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd",
            "yyyyMMdd",
        };
        if (DateTimeOffset.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value))
            return true;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
    }
}
