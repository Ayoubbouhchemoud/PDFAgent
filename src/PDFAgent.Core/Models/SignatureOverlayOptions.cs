namespace PDFAgent.Core.Models;

public enum SignaturePlacement { BottomLeft, BottomCenter, BottomRight }

public sealed record SignatureOverlayOptions
{
    public byte[] ImageBytes { get; init; } = [];
    public int PageNumber   { get; init; } = 1;
    public SignaturePlacement Placement    { get; init; } = SignaturePlacement.BottomRight;
    public double SignatureWidth  { get; init; } = 200;
    public double SignatureHeight { get; init; } = 80;
    public double Margin { get; init; } = 36;
}
