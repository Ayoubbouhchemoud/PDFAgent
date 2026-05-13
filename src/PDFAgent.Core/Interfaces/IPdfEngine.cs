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
    string FilePath { get; }
    bool IsOpen { get; }
    PdfDocumentInfo? DocumentInfo { get; }
}
