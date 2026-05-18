using CommunityToolkit.Mvvm.ComponentModel;

namespace PDFAgent.App.ViewModels;

/// <summary>Represents one word from the PDF that the user may edit in-place.</summary>
public sealed partial class TextEditWordViewModel : ObservableObject
{
    // ── Canvas display coordinates (WPF DIPs, 96 DPI reference) ──────────────

    /// <summary>Left edge in canvas DIPs.</summary>
    public double CanvasX { get; init; }
    /// <summary>Top edge in canvas DIPs.</summary>
    public double CanvasY { get; init; }
    public double CanvasWidth  { get; init; }
    public double CanvasHeight { get; init; }

    // ── PDF storage coordinates (PdfSharp pts, top-left origin) ──────────────

    public double PdfX      { get; init; }
    public double PdfY      { get; init; }
    public double PdfWidth  { get; init; }
    public double PdfHeight { get; init; }

    // ── Content ───────────────────────────────────────────────────────────────

    public string  OriginalText { get; init; } = "";
    public double  FontSize     { get; init; }
    public string? FontName     { get; init; }
    public bool    IsBold       { get; init; }
    public bool    IsItalic     { get; init; }

    [ObservableProperty]
    private string _editedText = "";

    public bool IsEdited => EditedText != OriginalText && !string.IsNullOrWhiteSpace(EditedText);
}
