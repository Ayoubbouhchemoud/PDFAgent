namespace PDFAgent.Core.Models;

/// <summary>
/// One ink stroke drawn on a PDF page.
/// All coordinates and thickness are in PDF points (72 pts = 1 inch, top-left origin, Y down).
/// </summary>
public sealed class DrawingStroke
{
    /// <summary>Ordered sequence of PDF-point positions along the stroke.</summary>
    public List<(double X, double Y)> Points { get; } = new();

    /// <summary>Line weight in PDF points.</summary>
    public double Thickness  { get; init; } = 2.0;

    public byte R { get; init; } = 0;
    public byte G { get; init; } = 0;
    public byte B { get; init; } = 0;

    /// <summary>Opacity 0 = fully transparent, 255 = fully opaque.</summary>
    public byte A { get; init; } = 255;

    /// <summary>1-based page number this stroke belongs to.</summary>
    public int PageNumber { get; init; }
}
