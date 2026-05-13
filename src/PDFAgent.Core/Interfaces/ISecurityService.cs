using PDFAgent.Core.Models;

namespace PDFAgent.Core.Interfaces;

public interface ISecurityService
{
    Task<OperationResult> EncryptAsync(string filePath, string outputPath, EncryptionOptions options, CancellationToken ct = default);
    Task<OperationResult> DecryptAsync(string filePath, string outputPath, string password, CancellationToken ct = default);
    Task<OperationResult<byte[]>> SignAsync(string filePath, SigningOptions options, CancellationToken ct = default);
    Task<OperationResult<bool>> VerifySignatureAsync(string filePath, CancellationToken ct = default);
}

public sealed record EncryptionOptions
{
    public string UserPassword { get; init; } = string.Empty;
    public string? OwnerPassword { get; init; }
    public bool AllowPrinting { get; init; } = true;
    public bool AllowModifying { get; init; }
    public bool AllowCopying { get; init; }
    public bool AllowAnnotations { get; init; }
    public int KeySize { get; init; } = 256;
}

public sealed record SigningOptions
{
    public string CertificatePath { get; init; } = string.Empty;
    public string? CertificatePassword { get; init; }
    public string? Reason { get; init; }
    public string? Location { get; init; }
    public string? ContactInfo { get; init; }
    public int PageNumber { get; init; } = 1;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 200;
    public double Height { get; init; } = 80;
}
