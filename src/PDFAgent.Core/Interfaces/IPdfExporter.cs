using PDFAgent.Core.Models;

namespace PDFAgent.Core.Interfaces;

/// <summary>
/// Converts a PDF file to another format.
/// Always operates on file paths — does not require an open document in IPdfEngine.
/// </summary>
public interface IPdfExporter
{
    /// <summary>
    /// Exports <paramref name="inputPath"/> to <paramref name="outputPath"/>.
    /// For multi-file outputs (PNG / JPG / SVG all-pages) <paramref name="outputPath"/>
    /// is a folder; for all other formats it is a file path.
    /// </summary>
    Task<OperationResult> ExportAsync(
        string inputPath,
        string outputPath,
        ExportFormat format,
        ExportOptions options,
        IProgress<(int Current, int Total)>? progress = null,
        CancellationToken ct = default);
}
