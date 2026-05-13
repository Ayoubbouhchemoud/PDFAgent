namespace PDFAgent.App.Services;

/// <summary>
/// Parses human-readable page range strings (1-based) into 0-based page indices.
/// Accepts: "1", "1,3", "1-5", "1,3-5,7", "all"
/// </summary>
public static class PageRangeParser
{
    public static IReadOnlyList<int> Parse(string rangeText, int totalPages)
    {
        if (string.IsNullOrWhiteSpace(rangeText) ||
            rangeText.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Range(0, totalPages).ToList();

        var pages = new SortedSet<int>();
        foreach (var part in rangeText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains('-'))
            {
                var segments = part.Split('-', 2);
                if (int.TryParse(segments[0], out var from) &&
                    int.TryParse(segments[1], out var to))
                {
                    from = Math.Clamp(from, 1, totalPages);
                    to   = Math.Clamp(to,   1, totalPages);
                    for (var p = Math.Min(from, to); p <= Math.Max(from, to); p++)
                        pages.Add(p - 1); // convert to 0-based
                }
            }
            else if (int.TryParse(part, out var single))
            {
                var idx = Math.Clamp(single, 1, totalPages) - 1;
                pages.Add(idx);
            }
        }

        return pages.ToList();
    }
}
