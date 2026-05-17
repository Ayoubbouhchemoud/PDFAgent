using PDFAgent.Core.Models;

namespace PDFAgent.Core.Interfaces;

/// <summary>
/// Converts a PDF file to another format.
/// One format is implemented at a time; unsupported formats return a failure result.
/// </summary>
public interface IPdfExporter
{
    Task<OperationResult> ExportAsync(
        string inputPath,
        string outputPath,
        ExportFormat format,
        CancellationToken ct = default);
}
