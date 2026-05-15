using PDFAgent.Core.Models;

namespace PDFAgent.Core.Interfaces;

public interface IPdfEngine : IAsyncDisposable
{
    Task<OperationResult<PdfDocumentInfo>> OpenAsync(string filePath, string? password = null, CancellationToken ct = default);
    Task<OperationResult> CloseAsync();
    Task<OperationResult<PdfPageInfo>> GetPageAsync(int pageNumber, CancellationToken ct = default);
    Task<OperationResult<IReadOnlyList<PdfPageInfo>>> GetPagesAsync(CancellationToken ct = default);
    Task<OperationResult<byte[]>> RenderPageAsync(int pageNumber, double dpi = 150, CancellationToken ct = default);
    Task<OperationResult<byte[]>> RenderThumbnailAsync(int pageNumber, int maxSize = 256, CancellationToken ct = default);
    Task<OperationResult<IReadOnlyList<PdfTextSegment>>> ExtractTextAsync(int pageNumber, CancellationToken ct = default);

    /// <summary>
    /// Opens <paramref name="filePath"/> independently of the currently loaded document,
    /// renders the first page as a thumbnail, and returns the PNG bytes + total page count.
    /// Safe to call while another document is open.
    /// </summary>
    Task<(byte[]? Thumbnail, int PageCount)> RenderExternalPreviewAsync(
        string filePath, int maxSize = 220, CancellationToken ct = default);
    string FilePath { get; }
    bool IsOpen { get; }
    PdfDocumentInfo? DocumentInfo { get; }
}
