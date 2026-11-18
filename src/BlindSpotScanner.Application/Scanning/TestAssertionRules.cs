namespace BlindSpotScanner.Application.Scanning;

/// <summary>D27 rules shared by the scan and the validator.</summary>
public static class TestAssertionRules
{
    /// <summary>
    /// Sent field paths (<c>lines[].bookId</c>) whose last segment no checked name matches, ignoring
    /// case; in <paramref name="sent"/> order.
    /// </summary>
    public static IReadOnlyList<string> UncheckedFields(IReadOnlyList<string> sent, IReadOnlyList<string> checkedFields)
    {
        var checkedNames = checkedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return sent.Where(path => !checkedNames.Contains(Leaf(path))).ToList();
    }

    private static string Leaf(string path) => path[(path.LastIndexOf('.') + 1)..].Replace("[]", string.Empty, StringComparison.Ordinal);
}
