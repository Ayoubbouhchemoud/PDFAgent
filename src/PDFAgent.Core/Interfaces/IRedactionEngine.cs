using PDFAgent.Core.Models;

namespace PDFAgent.Core.Interfaces;

public interface IRedactionEngine
{
    Task<OperationResult> RedactTextAsync(string filePath, string outputPath, IReadOnlyList<RedactionTarget> targets,
        RedactionOptions options, CancellationToken ct = default);
    Task<OperationResult> RedactPatternAsync(string filePath, string outputPath, string regexPattern,
        RedactionOptions options, CancellationToken ct = default);
    Task<OperationResult> RedactPiiAsync(string filePath, string outputPath, PiiRedactionProfile profile,
        CancellationToken ct = default);
    Task<OperationResult<RedactionReport>> PreviewRedactionAsync(string filePath,
        IReadOnlyList<RedactionTarget> targets, CancellationToken ct = default);
}

public sealed record RedactionTarget
{
    public int PageNumber { get; init; }
    public BoundingRect Bounds { get; init; } = default!;
    public string? TextMatch { get; init; }
    public RedactionTargetType TargetType { get; init; }
}

public enum RedactionTargetType { TextBounds, RegexMatch, PageRegion, Annotation }

public sealed record RedactionOptions
{
    public bool RemoveOverlappingContent { get; init; } = true;
    public string FillColorHex { get; init; } = "#000000";
    public bool AddRedactionMark { get; init; } = true;
    public bool RemoveMetadata { get; init; } = true;
    public string? Reason { get; init; }
    public string? Author { get; init; }
}

public sealed record RedactionReport
{
    public int TotalRedactions { get; init; }
    public IReadOnlyList<string> MatchedTexts { get; init; } = Array.Empty<string>();
    public int PagesAffected { get; init; }
    public long OriginalSize { get; init; }
    public long EstimatedOutputSize { get; init; }
}

public sealed record PiiRedactionProfile
{
    public string Name { get; init; } = "Default";
    public bool RedactEmails { get; init; } = true;
    public bool RedactPhoneNumbers { get; init; } = true;
    public bool RedactSsn { get; init; } = true;
    public bool RedactCreditCards { get; init; } = true;
    public bool RedactNames { get; init; }
    public bool RedactAddresses { get; init; }
    public bool RedactDates { get; init; }
    public IReadOnlyList<string> CustomPatterns { get; init; } = Array.Empty<string>();
}
