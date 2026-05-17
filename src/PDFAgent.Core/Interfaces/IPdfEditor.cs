using PDFAgent.Core.Models;

namespace PDFAgent.Core.Interfaces;

public interface IPdfEditor
{
    Task<OperationResult> MergeAsync(IReadOnlyList<string> filePaths, string outputPath, CancellationToken ct = default);
    Task<OperationResult> SplitAsync(string inputPath, SplitOptions opts, CancellationToken ct = default);
    Task<OperationResult> RotatePagesAsync(string filePath, IReadOnlyList<int> pageNumbers, int degrees, CancellationToken ct = default);
    Task<OperationResult> ExtractPagesAsync(string inputPath, string outputPath, IReadOnlyList<int> pageNumbers, CancellationToken ct = default);
    Task<OperationResult> InsertPagesAsync(string targetPath, string sourcePath, int insertAtIndex, CancellationToken ct = default);
    Task<OperationResult> DeletePagesAsync(string filePath, IReadOnlyList<int> pageNumbers, CancellationToken ct = default);
    Task<OperationResult> ReorderPagesAsync(string filePath, IReadOnlyList<int> newOrder, CancellationToken ct = default);
    Task<OperationResult> AddWatermarkAsync(string filePath, string outputPath, string text, CancellationToken ct = default);
    Task<OperationResult> AddStampAsync(string filePath, string outputPath, string stampText, CancellationToken ct = default);
    Task<OperationResult> AddPageAnnotationAsync(string filePath, string outputPath, int pageNumber, string text, CancellationToken ct = default);
    Task<OperationResult> AddSignatureImageAsync(string filePath, string outputPath, SignatureOverlayOptions opts, CancellationToken ct = default);
    Task<OperationResult> BakeTextAnnotationsAsync(string filePath, string outputPath, IReadOnlyList<TextAnnotationRecord> annotations, CancellationToken ct = default);
    Task<OperationResult> BakeTextEditsAsync(string filePath, string outputPath, IReadOnlyList<TextEditRecord> edits, CancellationToken ct = default);
    Task<OperationResult> AddBlankPageAsync(string filePath, string outputPath, int insertAtIndex, double widthPts, double heightPts, CancellationToken ct = default);

    /// <summary>
    /// Reduce the file size of a PDF.
    /// Pass <paramref name="imageDpi"/> = null for lossless stream re-compression (text preserved).
    /// Pass a DPI value to re-render every page as a JPEG image at that resolution.
    /// <paramref name="jpegQuality"/> (1–100) is ignored in lossless mode.
    /// </summary>
    Task<OperationResult> CompressAsync(
        string inputPath,
        string outputPath,
        int? imageDpi,
        int jpegQuality,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Convert a single file to PDF. Supports images (.png/.jpg/.jpeg/.bmp/.tiff)
    /// and plain text (.txt).
    /// </summary>
    Task<OperationResult> ConvertToPdfAsync(
        string inputPath,
        string outputPath,
        CancellationToken ct = default);

    /// <summary>Combine multiple image files into one multi-page PDF.</summary>
    Task<OperationResult> ConvertImagesToPdfAsync(
        IReadOnlyList<string> imagePaths,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}

public enum SplitMode { SplitAll, SplitRange, SplitEvery }

/// <summary>Carries all parameters for a split operation.</summary>
public sealed record SplitOptions
{
    public SplitMode Mode { get; init; } = SplitMode.SplitAll;
    public string OutputDir { get; init; } = string.Empty;
    public string OutputFile { get; init; } = string.Empty;
    public IReadOnlyList<int> PageIndices { get; init; } = Array.Empty<int>();
    public int EveryN { get; init; } = 2;
    public string BaseName { get; init; } = "page";
}
