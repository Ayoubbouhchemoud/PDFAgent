namespace PDFAgent.Core.Models;

public sealed record TextAnnotationRecord
{
    public int    PageNumber { get; init; }   // 1-based
    public double X         { get; init; }   // PDF points, top-left of bounding box
    public double Y         { get; init; }   // PDF points, top-left of bounding box
    public double Width     { get; init; } = 200;
    public double Height    { get; init; } = 40;
    public string Text      { get; init; } = "";
    public double FontSize  { get; init; } = 12;
}
