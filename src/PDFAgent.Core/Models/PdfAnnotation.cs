namespace PDFAgent.Core.Models;

public sealed record PdfAnnotation
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public AnnotationType Type { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Rotation { get; set; }
    public double Opacity { get; set; } = 1.0;
    public string? Text { get; set; }
    public string? Author { get; init; }
    public string ColorHex { get; set; } = "#FFEAA648";
    public int PageNumber { get; init; }
    public DateTime CreatedOn { get; init; } = DateTime.UtcNow;
    public bool IsLocked { get; set; }
}

public enum AnnotationType
{
    Highlight,
    Underline,
    Strikeout,
    Squiggle,
    FreeText,
    StickyNote,
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Polygon,
    Freehand,
    Stamp,
    Callout,
    Ink,
}
