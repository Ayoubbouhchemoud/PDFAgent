namespace PDFAgent.Core.Models;

public sealed record PdfDocumentInfo
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public long FileSizeBytes { get; init; }
    public int PageCount { get; init; }
    public double FileSizeKB => Math.Round(FileSizeBytes / 1024.0, 1);
    public double FileSizeMB => Math.Round(FileSizeBytes / (1024.0 * 1024.0), 2);
    public string? Title { get; init; }
    public string? Author { get; init; }
    public string? Subject { get; init; }
    public string? Producer { get; init; }
    public string? PdfVersion { get; init; }
    public bool IsEncrypted { get; init; }
    public bool IsLinearized { get; init; }
    public bool HasSignature { get; init; }
    public DateTime? CreatedOn { get; init; }
    public DateTime? ModifiedOn { get; init; }
}
