using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;

namespace PDFAgent.App.Services;

public interface IFileDialogService
{
    string? OpenPdf();
    IReadOnlyList<string> OpenMultiplePdfs();
    string? SavePdf(string defaultName);
    string? SelectFolder();
    string? OpenImage();
    string? OpenCertificate();
    string? SaveTextFile(string defaultName);
    string? SaveImageFile(string defaultName);
    void ShowProperties(PdfDocumentInfo info);
    void PrintFile(string filePath);

    /// <summary>Shows the Rotate Options dialog. Returns null if cancelled.</summary>
    RotateDialogResult? ShowRotateDialog(int currentPage, int totalPages);

    /// <summary>Shows the Split dialog. Returns null if cancelled.</summary>
    SplitDialogResult? ShowSplitDialog(int totalPages);

    /// <summary>
    /// Shows the Merge dialog pre-populated with <paramref name="initialFiles"/>.
    /// Returns the ordered list of file paths, or null if cancelled.
    /// The caller must then ask for an output path via <see cref="SavePdf"/>.
    /// </summary>
    IReadOnlyList<string>? ShowMergeDialog(IEnumerable<string> initialFiles);

    /// <summary>Shows the Add Page dialog. Returns null if cancelled.</summary>
    AddPageDialogResult? ShowAddPageDialog(int currentPage, int totalPages, double currentWidthPts, double currentHeightPts);

    /// <summary>Opens a file picker for "Convert to PDF" — images, Word, Excel, PowerPoint, TXT.</summary>
    IReadOnlyList<string> OpenForConversion();
}

/// <summary>Result returned by the Rotate Options dialog.</summary>
public sealed record RotateDialogResult(
    RotatePageSelection PageSelection,
    string PageRangeText,
    int Degrees);

public enum RotatePageSelection { All, CurrentPage, Range }

/// <summary>Result returned by the Split dialog.</summary>
public sealed record SplitDialogResult(
    SplitMode Mode,
    string PageRange,
    int EveryN);

public enum AddPagePosition { BeforeCurrent, AfterCurrent, AtBeginning, AtEnd }

/// <summary>Result returned by the Add Page dialog.</summary>
public sealed record AddPageDialogResult(AddPagePosition Position, double WidthPts, double HeightPts);
