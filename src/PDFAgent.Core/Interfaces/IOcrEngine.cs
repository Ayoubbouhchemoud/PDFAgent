using PDFAgent.Core.Models;

namespace PDFAgent.Core.Interfaces;

public interface IOcrEngine
{
    Task<OperationResult<OcrResult>> ProcessPageAsync(byte[] imageData, string language = "eng", CancellationToken ct = default);
    Task<OperationResult<IReadOnlyList<OcrResult>>> ProcessBatchAsync(IReadOnlyList<byte[]> images, string language = "eng",
        IProgress<double>? progress = null, CancellationToken ct = default);
    bool IsAvailable { get; }
    IReadOnlyList<string> SupportedLanguages { get; }
}

public sealed record OcrResult
{
    public string FullText { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public IReadOnlyList<OcrWord> Words { get; init; } = Array.Empty<OcrWord>();
    public IReadOnlyList<OcrParagraph> Paragraphs { get; init; } = Array.Empty<OcrParagraph>();
}

public sealed record OcrWord
{
    public string Text { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public BoundingRect Bounds { get; init; } = default!;
    public string? SuggestedCorrection { get; init; }
}

public sealed record OcrParagraph
{
    public string Text { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public BoundingRect Bounds { get; init; } = default!;
    public IReadOnlyList<OcrWord> Words { get; init; } = Array.Empty<OcrWord>();
}
