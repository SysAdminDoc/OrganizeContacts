using System.Globalization;
using System.Text;

namespace OrganizeContacts.Core.Normalize;

/// <summary>
/// Name normalization: diacritic strip, prefix/suffix removal, initial collapse.
/// Tuned for English-leaning address books while keeping non-Latin names intact
/// (only diacritics are stripped — base ideographs / Cyrillic stay).
/// </summary>
public static class NameNormalizer
{
    private static readonly HashSet<string> Prefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "miss", "mx", "dr", "sir", "lord", "lady",
        "rev", "fr", "br", "sr", "prof", "professor", "hon",
    };

    private static readonly HashSet<string> Suffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "jr", "sr", "ii", "iii", "iv", "v", "phd", "md", "esq", "cpa", "ph.d", "m.d",
    };

    /// <summary>Lowercase, accent-folded, prefix/suffix-stripped, single-spaced.</summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var foldedAccents = StripDiacritics(input);
        var lower = foldedAccents.Trim().ToLower(CultureInfo.InvariantCulture);

        // Drop punctuation except apostrophes which can be meaningful in names like O'Brien
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch) || ch == '\'' || ch == '-') sb.Append(ch);
            else if (char.IsWhiteSpace(ch) && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }

        var tokens = sb.ToString().Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Strip leading prefixes / trailing suffixes (and the dotted variants).
        while (tokens.Count > 0 && Prefixes.Contains(tokens[0].TrimEnd('.'))) tokens.RemoveAt(0);
        while (tokens.Count > 0 && Suffixes.Contains(tokens[^1].TrimEnd('.'))) tokens.RemoveAt(tokens.Count - 1);

        return string.Join(' ', tokens);
    }

    /// <summary>Tokens normalized for fuzzy matching (no initials, sorted optional).</summary>
    public static IReadOnlyList<string> Tokens(string? input, bool sorted = false)
    {
        var n = Normalize(input);
        if (string.IsNullOrWhiteSpace(n)) return Array.Empty<string>();
        var toks = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!sorted) return toks;
        Array.Sort(toks, StringComparer.Ordinal);
        return toks;
    }

    /// <summary>Collapse single-letter initials to their letter ("J." -> "j").</summary>
    public static string DropInitialDots(string s) =>
        string.IsNullOrEmpty(s) ? s : s.Replace(".", "");

    /// <summary>Unicode NFD + remove combining marks.</summary>
    public static string StripDiacritics(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var nfd = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(nfd.Length);
        foreach (var ch in nfd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Double Metaphone-ish phonetic key. Lightweight; good enough as a blocking signal.</summary>
    public static string Metaphone(string? input)
    {
        var s = Normalize(input);
        if (string.IsNullOrEmpty(s)) return string.Empty;
        s = new string(s.Where(c => char.IsLetter(c)).ToArray()).ToUpperInvariant();
        if (s.Length == 0) return string.Empty;

        // Tiny rule set: drop silent letters, collapse vowels, map common consonant pairs.
        s = s.Replace("PH", "F").Replace("CK", "K").Replace("KN", "N").Replace("WR", "R")
             .Replace("MB", "M").Replace("GH", "").Replace("SH", "X").Replace("CH", "X")
             .Replace("TH", "0").Replace("PS", "S");

        var sb = new StringBuilder();
        char? last = null;
        foreach (var c in s)
        {
            char m = c switch
            {
                'A' or 'E' or 'I' or 'O' or 'U' or 'Y' => sb.Length == 0 ? c : '\0',
                'B' or 'P' or 'V' or 'F' or 'W' => 'F',
                'C' or 'G' or 'J' or 'K' or 'Q' or 'X' or 'Z' or 'S' => 'K',
                'D' or 'T' => 'T',
                'L' => 'L',
                'M' or 'N' => 'M',
                'R' => 'R',
                'H' => '\0',
                _ => c,
            };
            if (m == '\0') continue;
            if (last == m) continue;
            sb.Append(m);
            last = m;
        }
        return sb.ToString();
    }
}
