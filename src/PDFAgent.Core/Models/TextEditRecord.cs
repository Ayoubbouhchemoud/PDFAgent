namespace PDFAgent.Core.Models;

/// <summary>
/// One word-level text replacement: white-out the original box and draw new text.
/// Coordinates are in PdfSharp points (top-left origin, Y increases downward).
/// </summary>
public sealed record TextEditRecord
{
    public int     PageNumber { get; init; }   // 1-based
    public double  X         { get; init; }
    public double  Y         { get; init; }
    public double  Width     { get; init; }
    public double  Height    { get; init; }
    public string  NewText   { get; init; } = "";
    public double  FontSize  { get; init; } = 12;
    public string? FontName  { get; init; }
    public bool    IsBold    { get; init; }
    public bool    IsItalic  { get; init; }
}
