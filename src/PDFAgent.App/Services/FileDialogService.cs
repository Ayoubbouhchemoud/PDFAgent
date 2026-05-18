using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using PDFAgent.Core.Models;

namespace PDFAgent.App.Services;

public sealed class FileDialogService : IFileDialogService
{
    private static Window Owner => Application.Current.MainWindow;

    public string? OpenPdf()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Title = "Open PDF",
            DefaultExt = ".pdf",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string> OpenMultiplePdfs()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Select PDFs to Merge",
            Multiselect = true,
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : Array.Empty<string>();
    }

    public string? SavePdf(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Save PDF as…",
            FileName = defaultName,
            DefaultExt = ".pdf",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SelectFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select output folder",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
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
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
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
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
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
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
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
        var dialog = new Views.MergeDialog { Owner = Application.Current.MainWindow };
        dialog.PreloadFiles(initialFiles);
        return dialog.ShowDialog() == true ? dialog.OrderedFiles : null;
    }

    public SplitDialogResult? ShowSplitDialog(int totalPages)
    {
        var dialog = new Views.SplitDialog { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return null;
        return new SplitDialogResult(dialog.SelectedMode, dialog.PageRange, dialog.EveryN);
    }

    public AddPageDialogResult? ShowAddPageDialog(int currentPage, int totalPages, double currentWidthPts, double currentHeightPts)
    {
        var dialog = new Views.AddPageDialog(currentPage, totalPages, currentWidthPts, currentHeightPts)
            { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return null;
        return new AddPageDialogResult(dialog.SelectedPosition, dialog.SelectedWidthPts, dialog.SelectedHeightPts);
    }

    public RotateDialogResult? ShowRotateDialog(int currentPage, int totalPages)
    {
        var dialog = new Views.RotateOptionsDialog { Owner = Application.Current.MainWindow };
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

    public string? SaveDocxFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter      = "Word document (*.docx)|*.docx|All files (*.*)|*.*",
            Title       = "Save Word Document as…",
            FileName    = defaultName,
            DefaultExt  = ".docx",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveHtmlFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "HTML file (*.html)|*.html|All files (*.*)|*.*",
            Title = "Save HTML as…", FileName = defaultName, DefaultExt = ".html",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveEpubFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "EPUB e-book (*.epub)|*.epub|All files (*.*)|*.*",
            Title = "Save EPUB as…", FileName = defaultName, DefaultExt = ".epub",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveSvgFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "SVG image (*.svg)|*.svg|All files (*.*)|*.*",
            Title = "Save SVG as…", FileName = defaultName, DefaultExt = ".svg",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SavePptxFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PowerPoint presentation (*.pptx)|*.pptx|All files (*.*)|*.*",
            Title = "Save PowerPoint as…", FileName = defaultName, DefaultExt = ".pptx",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveXlsxFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Title = "Save Excel workbook as…", FileName = defaultName, DefaultExt = ".xlsx",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SavePngFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png|All files (*.*)|*.*",
            Title = "Save PNG image as…", FileName = defaultName, DefaultExt = ".png",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveJpgFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JPEG image (*.jpg)|*.jpg|All files (*.*)|*.*",
            Title = "Save JPEG image as…", FileName = defaultName, DefaultExt = ".jpg",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveZipFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "ZIP archive (*.zip)|*.zip|All files (*.*)|*.*",
            Title = "Save pages as ZIP…", FileName = defaultName, DefaultExt = ".zip",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveMdFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Markdown file (*.md)|*.md|All files (*.*)|*.*",
            Title = "Save as Markdown…", FileName = defaultName, DefaultExt = ".md",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveTxtFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Save as plain text…", FileName = defaultName, DefaultExt = ".txt",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public PDFAgent.Core.Models.ProtectOptions? ShowSecurePdfDialog()
    {
        var dialog = new Views.SecurePdfDialog { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Options : null;
    }

    public string? ShowRemoveProtectionDialog()
    {
        var dialog = new Views.UnlockPdfDialog { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Password : null;
    }

    public string? ShowOpenPasswordDialog()
    {
        var dialog = new Views.OpenPasswordDialog { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Password : null;
    }

    public ExtractImagesDialogResult? ShowExtractImagesDialog(int currentPage, int totalPages)
    {
        var dialog = new Views.ExtractImagesDialog(currentPage, totalPages)
            { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return null;
        return new ExtractImagesDialogResult(dialog.Scope, dialog.PageRangeText, dialog.MinDimensionPx);
    }

    public IReadOnlyList<string> OpenForConversion()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Supported files (*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.tif;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.txt)|*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.tif;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.txt|All files (*.*)|*.*",
            Title = "Convert to PDF — Select File(s)",
            Multiselect = true,
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : Array.Empty<string>();
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
