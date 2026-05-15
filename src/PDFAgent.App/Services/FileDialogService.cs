using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using PDFAgent.Core.Models;

namespace PDFAgent.App.Services;

public sealed class FileDialogService : IFileDialogService
{
    public string? OpenPdf()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Title = "Open PDF",
            DefaultExt = ".pdf",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string> OpenMultiplePdfs()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Select PDFs to Merge",
            Multiselect = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }

    public string? SavePdf(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Save PDF",
            FileName = defaultName,
            DefaultExt = ".pdf",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectFolder()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select output folder",
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string? OpenImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.tiff;*.bmp)|*.png;*.jpg;*.jpeg;*.tiff;*.bmp",
            Title = "Select Image",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? OpenCertificate()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Certificate files (*.pfx;*.p12)|*.pfx;*.p12|All files (*.*)|*.*",
            Title = "Select Signing Certificate",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveTextFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Save OCR Text",
            FileName = defaultName,
            DefaultExt = ".txt",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveImageFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|All files (*.*)|*.*",
            Title = "Export Page as Image",
            FileName = defaultName,
            DefaultExt = ".png",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowProperties(PdfDocumentInfo info)
    {
        var created = info.CreatedOn.HasValue
            ? info.CreatedOn.Value.ToString("yyyy-MM-dd HH:mm")
            : "—";
        var modified = info.ModifiedOn.HasValue
            ? info.ModifiedOn.Value.ToString("yyyy-MM-dd HH:mm")
            : "—";

        var msg =
            $"File: {info.FileName}\n" +
            $"Size: {info.FileSizeKB:N1} KB ({info.FileSizeMB:N2} MB)\n" +
            $"Pages: {info.PageCount}\n" +
            $"PDF version: {info.PdfVersion ?? "—"}\n\n" +
            $"Title: {info.Title ?? "—"}\n" +
            $"Author: {info.Author ?? "—"}\n" +
            $"Subject: {info.Subject ?? "—"}\n" +
            $"Producer: {info.Producer ?? "—"}\n\n" +
            $"Created: {created}\n" +
            $"Modified: {modified}\n" +
            $"Encrypted: {(info.IsEncrypted ? "Yes" : "No")}\n" +
            $"Has signature: {(info.HasSignature ? "Yes" : "No")}";

        MessageBox.Show(msg, $"Properties — {info.FileName}",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public IReadOnlyList<string>? ShowMergeDialog(IEnumerable<string> initialFiles)
    {
        var dialog = new Views.MergeDialog();
        dialog.PreloadFiles(initialFiles);
        return dialog.ShowDialog() == true ? dialog.OrderedFiles : null;
    }

    public SplitDialogResult? ShowSplitDialog(int totalPages)
    {
        var dialog = new Views.SplitDialog();
        if (dialog.ShowDialog() != true) return null;
        return new SplitDialogResult(dialog.SelectedMode, dialog.PageRange, dialog.EveryN);
    }

    public AddPageDialogResult? ShowAddPageDialog(int currentPage, int totalPages, double currentWidthPts, double currentHeightPts)
    {
        var dialog = new Views.AddPageDialog(currentPage, totalPages, currentWidthPts, currentHeightPts);
        if (dialog.ShowDialog() != true) return null;
        return new AddPageDialogResult(dialog.SelectedPosition, dialog.SelectedWidthPts, dialog.SelectedHeightPts);
    }

    public RotateDialogResult? ShowRotateDialog(int currentPage, int totalPages)
    {
        var dialog = new Views.RotateOptionsDialog();
        dialog.CurrentPageNumber = currentPage;
        if (dialog.ShowDialog() != true) return null;

        return new RotateDialogResult(
            dialog.PageScope switch
            {
                Views.RotatePageScope.All         => RotatePageSelection.All,
                Views.RotatePageScope.CurrentPage => RotatePageSelection.CurrentPage,
                Views.RotatePageScope.Range       => RotatePageSelection.Range,
                _                                 => RotatePageSelection.All,
            },
            dialog.PageRangeText,
            dialog.Degrees);
    }

    public void PrintFile(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                Verb = "print",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to print the document:\n{ex.Message}\n\nEnsure a default PDF reader is installed.",
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
