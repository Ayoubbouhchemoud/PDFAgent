namespace PDFAgent.Core.Models;

/// <summary>
/// Settings for PDF password-protection and permission restrictions.
/// All boolean permission flags default to the most permissive state (true)
/// so that only what the caller explicitly tightens changes.
/// </summary>
public sealed record ProtectOptions
{
    /// <summary>Password required to open the file. Null = no open-password required.</summary>
    public string? UserPassword  { get; init; }

    /// <summary>Password required to change security settings. Null = auto-generated internally.</summary>
    public string? OwnerPassword { get; init; }

    public bool AllowPrint             { get; init; } = true;
    public bool AllowHighQualityPrint  { get; init; } = true;
    public bool AllowCopyText          { get; init; } = true;
    public bool AllowModify            { get; init; } = false;
    public bool AllowFillForms         { get; init; } = true;
    public bool AllowAnnotations       { get; init; } = true;

    /// <summary>
    /// True → AES-256 (PDF 1.7, recommended).
    /// False → RC4-128 (PDF 1.4, legacy compatibility).
    /// </summary>
    public bool Use256BitAes { get; init; } = true;
}
