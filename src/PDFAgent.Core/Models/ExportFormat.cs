namespace PDFAgent.Core.Models;

/// <summary>
/// Output formats supported by the PDF export pipeline.
/// Formats are added here only once a correct implementation exists.
/// </summary>
public enum ExportFormat
{
    Html,
    Docx,
    Xlsx,
    Png,
    Jpg,
    Svg,
    Md,
    Txt,
}
