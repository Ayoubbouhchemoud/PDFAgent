using System.Collections.ObjectModel;
using System.IO;
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
    private readonly FileDialogService _fileDialog;

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
        FileDialogService fileDialog)
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

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenFileAsync(string? filePath = null)
    {
        filePath ??= _fileDialog.OpenPdf();
        if (string.IsNullOrEmpty(filePath)) return;

        IsBusy = true;
        StatusText = $"Opening {Path.GetFileName(filePath)}...";

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

    [RelayCommand]
    private async Task SaveFileAsync()
    {
        if (!_pdfEngine.IsOpen) return;
        var path = _fileDialog.SavePdf(DocumentInfo?.FileName ?? "output.pdf");
        if (path == null) return;

        IsBusy = true;
        try
        {
            File.Copy(_pdfEngine.FilePath, path, overwrite: true);
            StatusText = $"Saved to {Path.GetFileName(path)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

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

    [RelayCommand]
    private async Task MergeAsync()
    {
        var files = _fileDialog.OpenMultiplePdfs();
        if (files.Count < 2) return;

        var output = _fileDialog.SavePdf("merged.pdf");
        if (output == null) return;

        IsBusy = true;
        StatusText = "Merging PDFs...";
        try
        {
            var result = await _pdfEditor.MergeAsync(files, output);
            StatusText = result.IsSuccess ? "Merge complete" : $"Merge failed: {result.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SplitAsync()
    {
        if (!_pdfEngine.IsOpen) return;
        var dir = _fileDialog.SelectFolder();
        if (dir == null) return;

        IsBusy = true;
        StatusText = "Splitting...";
        try
        {
            var result = await _pdfEditor.SplitAsync(_pdfEngine.FilePath, dir, SplitMode.SplitAll);
            StatusText = result.IsSuccess ? "Split complete" : $"Split failed: {result.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RotateAsync()
    {
        if (!_pdfEngine.IsOpen) return;
        IsBusy = true;
        try
        {
            var result = await _pdfEditor.RotatePagesAsync(_pdfEngine.FilePath,
                Enumerable.Range(0, TotalPages).ToList(), 90);
            StatusText = result.IsSuccess ? "Rotation complete" : $"Rotation failed: {result.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OcrAsync()
    {
        if (!_pdfEngine.IsOpen) return;

        IsBusy = true;
        StatusText = "Running OCR...";
        try
        {
            for (var i = 0; i < TotalPages; i++)
            {
                var renderResult = await _pdfEngine.RenderPageAsync(i, dpi: 300);
                if (!renderResult.IsSuccess || renderResult.Value == null) continue;

                var ocrResult = await _ocrEngine.ProcessPageAsync(renderResult.Value);
                if (ocrResult.IsSuccess && ocrResult.Value != null)
                {
                    _logger.LogInformation("OCR page {Page}: {TextLength} chars, {Confidence:P} confidence",
                        i + 1, ocrResult.Value.FullText.Length, ocrResult.Value.Confidence / 100.0);
                }
            }

            StatusText = "OCR complete";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RedactAsync()
    {
        if (!_pdfEngine.IsOpen) return;
        var output = _fileDialog.SavePdf("redacted_output.pdf");
        if (output == null) return;

        IsBusy = true;
        StatusText = "Redacting...";
        try
        {
            var targets = new List<RedactionTarget>();
            var result = await _redactionEngine.RedactPiiAsync(
                _pdfEngine.FilePath, output, new PiiRedactionProfile());
            StatusText = result.IsSuccess ? "Redaction complete" : $"Redaction failed: {result.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignAsync()
    {
        // Placeholder: will use X509 certificate
        StatusText = "Digital signing — requires certificate setup";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void Annotate()
    {
        StatusText = "Annotation mode — coming soon";
    }

    [RelayCommand]
    private void EditText()
    {
        StatusText = "Text editing mode — coming soon";
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
