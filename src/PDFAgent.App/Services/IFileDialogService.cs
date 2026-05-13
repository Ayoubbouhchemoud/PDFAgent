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
}
