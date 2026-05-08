namespace OrganizeContacts.Core.Normalize;

public static class Levenshtein
{
    /// <summary>Computes the edit distance between two strings (case-sensitive).</summary>
    public static int Distance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Similarity in [0, 1] derived from edit distance.</summary>
    public static double Similarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1.0;
        var max = Math.Max(a?.Length ?? 0, b?.Length ?? 0);
        if (max == 0) return 1.0;
        return 1.0 - ((double)Distance(a ?? "", b ?? "") / max);
    }
}
