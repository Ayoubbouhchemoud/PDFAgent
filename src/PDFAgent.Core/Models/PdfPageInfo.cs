using PDFAgent.Core.Interfaces;

namespace PDFAgent.Core.Models;

public sealed record PdfPageInfo
{
    public int PageNumber { get; init; }
    public double WidthPoints { get; init; }
    public double HeightPoints { get; init; }
    public double WidthInches => WidthPoints / 72.0;
    public double HeightInches => HeightPoints / 72.0;
    public IRenderSurface? Thumbnail { get; set; }
    public IReadOnlyList<PdfTextSegment>? TextSegments { get; init; }
    public IReadOnlyList<PdfAnnotation>? Annotations { get; init; }
    public bool HasImages { get; init; }
    public bool HasVectorGraphics { get; init; }
}
