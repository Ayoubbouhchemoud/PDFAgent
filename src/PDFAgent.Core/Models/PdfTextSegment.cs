namespace PDFAgent.Core.Models;

public sealed record PdfTextSegment
{
    public string Text { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double FontSize { get; init; }
    public string? FontName { get; init; }
    public bool IsBold { get; init; }
    public bool IsItalic { get; init; }
    public int PageNumber { get; init; }
    public int SegmentIndex { get; init; }

    public BoundingRect Bounds => new(X, Y, Width, Height);
}

public sealed record BoundingRect(double X, double Y, double Width, double Height);

public sealed record GlyphInfo
{
    public char Character { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double AdvanceWidth { get; init; }
    public int GlyphIndex { get; init; }
    public string? FontName { get; init; }
}

public sealed record LineInfo
{
    public IReadOnlyList<GlyphInfo> Glyphs { get; init; } = Array.Empty<GlyphInfo>();
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double BaselineY { get; init; }
    public string? FontName { get; init; }
    public double FontSize { get; init; }
}
