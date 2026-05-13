using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PDFAgent.App.Services;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;

namespace PDFAgent.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly IPdfEngine _pdfEngine;
    private readonly IPdfEditor _pdfEditor;
    private readonly IOcrEngine _ocrEngine;
    private readonly IRedactionEngine _redactionEngine;
    private readonly IFileDialogService _fileDialog;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private PdfDocumentInfo? _documentInfo;

    [ObservableProperty]
    private PdfPageInfo? _selectedPage;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages;

    [ObservableProperty]
    private double _currentZoom = 1.0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(SplitCommand))]
    [NotifyCanExecuteChangedFor(nameof(RotateCommand))]
    [NotifyCanExecuteChangedFor(nameof(OcrCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedactCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnnotateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportToImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    [NotifyCanExecuteChangedFor(nameof(PropertiesCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _searchText;

    public ObservableCollection<ThumbnailItem> Thumbnails { get; } = new();
    public ObservableCollection<RenderedPageItem> RenderedPages { get; } = new();
    public List<double> ZoomLevels { get; } = new() { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0 };

    public MainViewModel(
        ILogger<MainViewModel> logger,
        IPdfEngine pdfEngine,
        IPdfEditor pdfEditor,
        IOcrEngine ocrEngine,
        IRedactionEngine redactionEngine,
        IFileDialogService fileDialog)
    {
        _logger = logger;
        _pdfEngine = pdfEngine;
        _pdfEditor = pdfEditor;
        _ocrEngine = ocrEngine;
        _redactionEngine = redactionEngine;
        _fileDialog = fileDialog;
    }

    public async Task InitializeAsync()
    {
        if (AppArguments.TryGetFilePath(out var filePath) && filePath != null)
            await OpenFileAsync(filePath);
    }

    private bool DocumentReady() => IsDocumentLoaded && !IsBusy;

    // ── File ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenFileAsync(string? filePath = null)
    {
        filePath ??= _fileDialog.OpenPdf();
        if (string.IsNullOrEmpty(filePath)) return;

        IsBusy = true;
        StatusText = $"Opening {Path.GetFileName(filePath)}…";
        try
        {
            if (_pdfEngine.IsOpen)
                await _pdfEngine.CloseAsync();

            var result = await _pdfEngine.OpenAsync(filePath);
            if (!result.IsSuccess || result.Value == null)
            {
                StatusText = $"Failed: {result.Message}";
                return;
            }

            DocumentInfo = result.Value;
            TotalPages = result.Value.PageCount;
            CurrentPage = 1;
            StatusText = $"Opened {result.Value.FileName} ({result.Value.PageCount} pages)";

            await LoadThumbnailsAsync();
            await RenderCurrentPagesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task SaveFileAsync()
    {
        var path = _fileDialog.SavePdf(DocumentInfo?.FileName ?? "output.pdf");
        if (path == null) return;

        IsBusy = true;
        try
        {
            await Task.Run(() => File.Copy(_pdfEngine.FilePath, path, overwrite: true));
            StatusText = $"Saved to {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Edit ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task MergeAsync()
    {
        var files = _fileDialog.OpenMultiplePdfs();
        if (files.Count < 2) return;

        var output = _fileDialog.SavePdf("merged.pdf");
        if (output == null) return;

        IsBusy = true;
        StatusText = "Merging PDFs…";
        try
        {
            var result = await _pdfEditor.MergeAsync(files, output);
            StatusText = result.IsSuccess ? $"Merge complete — {result.Message}" : $"Merge failed: {result.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task SplitAsync()
    {
        var dir = _fileDialog.SelectFolder();
        if (dir == null) return;

        IsBusy = true;
        StatusText = "Splitting…";
        try
        {
            var result = await _pdfEditor.SplitAsync(_pdfEngine.FilePath, dir, SplitMode.SplitAll);
            StatusText = result.IsSuccess ? $"Split complete — {result.Message}" : $"Split failed: {result.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task RotateAsync()
    {
        IsBusy = true;
        StatusText = "Rotating pages…";
        try
        {
            var pages = Enumerable.Range(0, TotalPages).ToList();
            var result = await _pdfEditor.RotatePagesAsync(_pdfEngine.FilePath, pages, 90);
            StatusText = result.IsSuccess ? $"Rotation complete — {result.Message}" : $"Rotation failed: {result.Message}";

            if (result.IsSuccess)
            {
                // Re-open to reflect rotation in viewer
                var path = _pdfEngine.FilePath;
                await _pdfEngine.CloseAsync();
                var reopen = await _pdfEngine.OpenAsync(path);
                if (reopen.IsSuccess && reopen.Value != null)
                {
                    DocumentInfo = reopen.Value;
                    await LoadThumbnailsAsync();
                    await RenderCurrentPagesAsync();
                }
            }
        }
        finally { IsBusy = false; }
    }

    // ── Tools ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task OcrAsync()
    {
        IsBusy = true;
        StatusText = "Running OCR…";
        try
        {
            var sb = new StringBuilder();
            for (var i = 0; i < TotalPages; i++)
            {
                StatusText = $"OCR: processing page {i + 1} of {TotalPages}…";
                var renderResult = await _pdfEngine.RenderPageAsync(i, dpi: 300);
                if (!renderResult.IsSuccess || renderResult.Value == null) continue;

                var ocrResult = await _ocrEngine.ProcessPageAsync(renderResult.Value);
                if (ocrResult.IsSuccess && ocrResult.Value != null)
                {
                    sb.AppendLine($"=== Page {i + 1} ===");
                    sb.AppendLine(ocrResult.Value.FullText);
                    sb.AppendLine();
                    _logger.LogInformation("OCR p{Page}: {Chars} chars, {Conf:P0} confidence",
                        i + 1, ocrResult.Value.FullText.Length, ocrResult.Value.Confidence / 100.0);
                }
            }

            var outputPath = _fileDialog.SaveTextFile(
                Path.GetFileNameWithoutExtension(DocumentInfo!.FileName) + "_ocr.txt");
            if (outputPath != null)
            {
                await File.WriteAllTextAsync(outputPath, sb.ToString());
                StatusText = $"OCR complete — saved to {Path.GetFileName(outputPath)}";
            }
            else
            {
                StatusText = "OCR complete (not saved)";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR failed");
            StatusText = $"OCR error: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task RedactAsync()
    {
        var output = _fileDialog.SavePdf(
            Path.GetFileNameWithoutExtension(DocumentInfo!.FileName) + "_redacted.pdf");
        if (output == null) return;

        IsBusy = true;
        StatusText = "Redacting PII…";
        try
        {
            var result = await _redactionEngine.RedactPiiAsync(
                _pdfEngine.FilePath, output, new PiiRedactionProfile
                {
                    RedactEmails = true,
                    RedactPhoneNumbers = true,
                    RedactSsn = true,
                    RedactCreditCards = true,
                });
            StatusText = result.IsSuccess
                ? $"Redaction complete — {result.Message}"
                : $"Redaction failed: {result.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task SignAsync()
    {
        var output = _fileDialog.SavePdf(
            Path.GetFileNameWithoutExtension(DocumentInfo!.FileName) + "_signed.pdf");
        if (output == null) return;

        IsBusy = true;
        StatusText = "Applying signature stamp…";
        try
        {
            var signedBy = Environment.UserName;
            var signedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            var stampText = $"SIGNED — {signedBy} — {signedAt}";

            var result = await _pdfEditor.AddStampAsync(_pdfEngine.FilePath, output, stampText);
            StatusText = result.IsSuccess
                ? $"Signed — saved to {Path.GetFileName(output)}"
                : $"Signing failed: {result.Message}";

            if (result.IsSuccess)
                _logger.LogInformation("Document signed by {User} → {Output}", signedBy, output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sign failed");
            StatusText = $"Sign error: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task AnnotateAsync()
    {
        var output = _fileDialog.SavePdf(
            Path.GetFileNameWithoutExtension(DocumentInfo!.FileName) + "_annotated.pdf");
        if (output == null) return;

        IsBusy = true;
        StatusText = "Adding annotation…";
        try
        {
            var annotationText = $"Reviewed — {Environment.UserName} — {DateTime.Now:yyyy-MM-dd}";
            var result = await _pdfEditor.AddPageAnnotationAsync(
                _pdfEngine.FilePath, output, CurrentPage, annotationText);
            StatusText = result.IsSuccess
                ? $"Annotation added to page {CurrentPage} — saved to {Path.GetFileName(output)}"
                : $"Annotation failed: {result.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Annotate failed");
            StatusText = $"Annotation error: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task ExportToImageAsync()
    {
        var baseName = Path.GetFileNameWithoutExtension(DocumentInfo!.FileName);
        var output = _fileDialog.SaveImageFile($"{baseName}_page{CurrentPage}.png");
        if (output == null) return;

        IsBusy = true;
        StatusText = $"Exporting page {CurrentPage} as image…";
        try
        {
            var result = await _pdfEngine.RenderPageAsync(CurrentPage - 1, dpi: 150);
            if (!result.IsSuccess || result.Value == null)
            {
                StatusText = $"Export failed: {result.Message}";
                return;
            }

            await File.WriteAllBytesAsync(output, result.Value);
            StatusText = $"Exported page {CurrentPage} → {Path.GetFileName(output)}";
            _logger.LogInformation("Exported page {Page} as image → {Output}", CurrentPage, output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExportToImage failed");
            StatusText = $"Export error: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private void Print()
    {
        _fileDialog.PrintFile(_pdfEngine.FilePath);
        StatusText = $"Print job sent for {DocumentInfo!.FileName}";
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private void Properties()
    {
        _fileDialog.ShowProperties(DocumentInfo!);
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    private async Task LoadThumbnailsAsync()
    {
        Thumbnails.Clear();
        for (var i = 0; i < TotalPages; i++)
        {
            var result = await _pdfEngine.RenderThumbnailAsync(i);
            Thumbnails.Add(new ThumbnailItem
            {
                PageNumber = i + 1,
                Thumbnail = result.Value,
                IsSelected = i == 0,
            });
        }
    }

    private async Task RenderCurrentPagesAsync()
    {
        RenderedPages.Clear();
        for (var i = 0; i < TotalPages; i++)
        {
            var result = await _pdfEngine.RenderPageAsync(i, dpi: CurrentZoom * 72);
            RenderedPages.Add(new RenderedPageItem
            {
                PageNumber = i + 1,
                ImageData = result.Value ?? Array.Empty<byte>(),
            });
        }
    }
}

public sealed class ThumbnailItem
{
    public int PageNumber { get; set; }
    public byte[]? Thumbnail { get; set; }
    public bool IsSelected { get; set; }
}

public sealed class RenderedPageItem
{
    public int PageNumber { get; set; }
    public byte[] ImageData { get; set; } = Array.Empty<byte>();
}
