namespace PDFAgent.Core.Models;

public enum ExportFormat
{
    Txt,
    Html,
    Epub,
    Png,
    Jpg,
    Svg,
    Pdf,
    PdfA1b,
    PdfA2b,
    PdfA3b,
    SecurePdf,
    Docx,
    Pptx,
    Xlsx,
}

public sealed record ExportOptions
{
    public int    Dpi           { get; init; } = 150;
    public bool   AllPages      { get; init; } = true;
    public int    PageIndex     { get; init; } = 0;
    public string? UserPassword { get; init; }
    public string? OwnerPassword{ get; init; }
}
