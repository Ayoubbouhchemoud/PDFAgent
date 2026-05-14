using PDFAgent.Core.Models;

namespace PDFAgent.Core.Interfaces;

public interface IPdfEditor
{
    Task<OperationResult> MergeAsync(IReadOnlyList<string> filePaths, string outputPath, CancellationToken ct = default);
    Task<OperationResult> SplitAsync(string inputPath, string outputDir, SplitMode mode, CancellationToken ct = default);
    Task<OperationResult> RotatePagesAsync(string filePath, IReadOnlyList<int> pageNumbers, int degrees, CancellationToken ct = default);
    Task<OperationResult> ExtractPagesAsync(string inputPath, string outputPath, IReadOnlyList<int> pageNumbers, CancellationToken ct = default);
    Task<OperationResult> InsertPagesAsync(string targetPath, string sourcePath, int insertAtIndex, CancellationToken ct = default);
    Task<OperationResult> DeletePagesAsync(string filePath, IReadOnlyList<int> pageNumbers, CancellationToken ct = default);
    Task<OperationResult> ReorderPagesAsync(string filePath, IReadOnlyList<int> newOrder, CancellationToken ct = default);
    Task<OperationResult> AddWatermarkAsync(string filePath, string outputPath, string text, CancellationToken ct = default);
    Task<OperationResult> AddStampAsync(string filePath, string outputPath, string stampText, CancellationToken ct = default);
    Task<OperationResult> AddPageAnnotationAsync(string filePath, string outputPath, int pageNumber, string text, CancellationToken ct = default);
    Task<OperationResult> AddSignatureImageAsync(string filePath, string outputPath, SignatureOverlayOptions opts, CancellationToken ct = default);
}

public enum SplitMode
{
    SplitAll,
    SplitRange,
    SplitEvery,
    SplitByBookmark,
}
