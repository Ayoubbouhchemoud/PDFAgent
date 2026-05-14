namespace PDFAgent.Core.Models;

public enum SignaturePlacement { BottomLeft, BottomCenter, BottomRight }

public sealed record SignatureOverlayOptions
{
    public byte[]             ImageBytes      { get; init; } = [];
    public int                PageNumber      { get; init; } = 1;
    public SignaturePlacement Placement       { get; init; } = SignaturePlacement.BottomRight;
    public double             SignatureWidth  { get; init; } = 200;
    public double             SignatureHeight { get; init; } = 80;
    public double             Margin          { get; init; } = 36;

    // Absolute PDF-point coordinates (top-left corner of the signature box).
    // Set by the sticker commit path; overrides everything else when non-null.
    public double? AbsoluteX { get; init; }
    public double? AbsoluteY { get; init; }

    // Legacy: normalised 0-1 fraction (centre of signature) — kept for compatibility.
    public double? CustomX { get; init; }
    public double? CustomY { get; init; }
}
