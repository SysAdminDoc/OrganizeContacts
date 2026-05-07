namespace OrganizeContacts.Core.Models;

public enum PhoneKind { Other, Mobile, Home, Work, Fax, Pager, Main }

public sealed class PhoneNumber
{
    public string Raw { get; init; } = string.Empty;
    public string Digits { get; init; } = string.Empty;
    public string? E164 { get; set; }
    public PhoneKind Kind { get; init; } = PhoneKind.Other;
    public bool IsPreferred { get; init; }
    public Guid? SourceId { get; init; }

    public static PhoneNumber Parse(string raw, PhoneKind kind = PhoneKind.Other, bool preferred = false, Guid? sourceId = null)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return new PhoneNumber { Raw = raw, Digits = digits, Kind = kind, IsPreferred = preferred, SourceId = sourceId };
    }
}
