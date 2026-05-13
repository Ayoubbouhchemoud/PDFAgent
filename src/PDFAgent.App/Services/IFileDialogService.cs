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

    /// <summary>
    /// Shows the Rotate Options dialog.
    /// Returns the user's choices, or null if the dialog was cancelled.
    /// </summary>
    RotateDialogResult? ShowRotateDialog(int currentPage, int totalPages);
}

/// <summary>Result returned by the Rotate Options dialog.</summary>
public sealed record RotateDialogResult(
    RotatePageSelection PageSelection,
    string PageRangeText,
    int Degrees);

public enum RotatePageSelection { All, CurrentPage, Range }
