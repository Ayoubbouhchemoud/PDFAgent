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
}

public enum SplitMode { SplitAll, SplitRange, SplitEvery }

/// <summary>Carries all parameters for a split operation.</summary>
public sealed record SplitOptions
{
    public SplitMode Mode { get; init; } = SplitMode.SplitAll;
    /// <summary>Output folder — used by SplitAll and SplitEvery.</summary>
    public string OutputDir { get; init; } = string.Empty;
    /// <summary>Output file path — used by SplitRange (extract to one PDF).</summary>
    public string OutputFile { get; init; } = string.Empty;
    /// <summary>0-based page indices to extract — used by SplitRange.</summary>
    public IReadOnlyList<int> PageIndices { get; init; } = Array.Empty<int>();
    /// <summary>Group size — used by SplitEvery.</summary>
    public int EveryN { get; init; } = 2;
    /// <summary>Base name for output files (without extension).</summary>
    public string BaseName { get; init; } = "page";
}
