using Microsoft.Win32;

namespace PDFAgent.App.Services;

public sealed class FileDialogService
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
}
