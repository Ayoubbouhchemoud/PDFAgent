using Microsoft.Extensions.Logging;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;

namespace PDFAgent.PdfEngine;

/// <summary>
/// PDF editing operations. PdfiumViewer is a read-only renderer, so write operations
/// require an additional library (e.g. iText7 or PdfSharp). Methods return a clear
/// "not implemented" result rather than throwing.
/// </summary>
public sealed class PdfiumEditor : IPdfEditor
{
    private readonly ILogger<PdfiumEditor> _logger;

    public PdfiumEditor(ILogger<PdfiumEditor> logger)
    {
        _logger = logger;
    }

    public Task<OperationResult> MergeAsync(
        IReadOnlyList<string> filePaths, string outputPath, CancellationToken ct = default)
    {
        _logger.LogWarning("Merge not implemented — add iText7 or PdfSharp for write support");
        return Task.FromResult(OperationResult.Fail("Merge requires a write-capable PDF library"));
    }

    public Task<OperationResult> SplitAsync(
        string inputPath, string outputDir, SplitMode mode, CancellationToken ct = default)
    {
        _logger.LogWarning("Split not implemented — add iText7 or PdfSharp for write support");
        return Task.FromResult(OperationResult.Fail("Split requires a write-capable PDF library"));
    }

    public Task<OperationResult> RotatePagesAsync(
        string filePath, IReadOnlyList<int> pageNumbers, int degrees, CancellationToken ct = default)
    {
        _logger.LogWarning("Rotate not implemented — add iText7 or PdfSharp for write support");
        return Task.FromResult(OperationResult.Fail("Rotate requires a write-capable PDF library"));
    }

    public Task<OperationResult> ExtractPagesAsync(
        string inputPath, string outputPath, IReadOnlyList<int> pageNumbers, CancellationToken ct = default)
    {
        _logger.LogWarning("ExtractPages not implemented — add iText7 or PdfSharp for write support");
        return Task.FromResult(OperationResult.Fail("ExtractPages requires a write-capable PDF library"));
    }

    public Task<OperationResult> InsertPagesAsync(
        string targetPath, string sourcePath, int insertAtIndex, CancellationToken ct = default)
    {
        _logger.LogWarning("InsertPages not implemented — add iText7 or PdfSharp for write support");
        return Task.FromResult(OperationResult.Fail("InsertPages requires a write-capable PDF library"));
    }

    public Task<OperationResult> DeletePagesAsync(
        string filePath, IReadOnlyList<int> pageNumbers, CancellationToken ct = default)
    {
        _logger.LogWarning("DeletePages not implemented — add iText7 or PdfSharp for write support");
        return Task.FromResult(OperationResult.Fail("DeletePages requires a write-capable PDF library"));
    }

    public Task<OperationResult> ReorderPagesAsync(
        string filePath, IReadOnlyList<int> newOrder, CancellationToken ct = default)
    {
        _logger.LogWarning("ReorderPages not implemented — add iText7 or PdfSharp for write support");
        return Task.FromResult(OperationResult.Fail("ReorderPages requires a write-capable PDF library"));
    }
}
