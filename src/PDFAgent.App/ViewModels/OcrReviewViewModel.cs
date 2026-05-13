using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;

namespace PDFAgent.App.ViewModels;

public sealed partial class OcrReviewViewModel : ObservableObject
{
    private readonly ILogger<OcrReviewViewModel> _logger;
    private readonly IPdfEngine _pdfEngine;
    private readonly IOcrEngine _ocrEngine;

    private readonly List<OcrPageResult> _pageResults = new();

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages;

    [ObservableProperty]
    private string _ocrText = string.Empty;

    [ObservableProperty]
    private byte[]? _pageImageData;

    [ObservableProperty]
    private double _confidence;

    [ObservableProperty]
    private int _wordCount;

    [ObservableProperty]
    private int _lowConfidenceWordCount;

    [ObservableProperty]
    private bool _showLowConfidenceHighlights;

    [ObservableProperty]
    private double _imageZoomWidth = 600;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isProcessing;

    public event EventHandler? CloseRequested;

    public OcrReviewViewModel(
        ILogger<OcrReviewViewModel> logger,
        IPdfEngine pdfEngine,
        IOcrEngine ocrEngine)
    {
        _logger = logger;
        _pdfEngine = pdfEngine;
        _ocrEngine = ocrEngine;
    }

    public async Task LoadAsync()
    {
        if (!_pdfEngine.IsOpen || _pdfEngine.DocumentInfo is null) return;

        TotalPages = _pdfEngine.DocumentInfo.PageCount;
        _pageResults.Clear();

        for (var i = 0; i < TotalPages; i++)
            _pageResults.Add(new OcrPageResult { PageIndex = i });

        await LoadPageAsync(0);
    }

    private async Task LoadPageAsync(int zeroBasedIndex)
    {
        IsProcessing = true;
        StatusMessage = $"Loading page {zeroBasedIndex + 1}…";

        try
        {
            var renderResult = await _pdfEngine.RenderPageAsync(zeroBasedIndex, dpi: 200);
            if (renderResult.IsSuccess)
                PageImageData = renderResult.Value;

            var cached = _pageResults[zeroBasedIndex];
            if (cached.OcrResult is null)
            {
                var ocrResult = await _ocrEngine.ProcessPageAsync(PageImageData ?? Array.Empty<byte>());
                if (ocrResult.IsSuccess && ocrResult.Value != null)
                {
                    cached.OcrResult = ocrResult.Value;
                    cached.EditedText = ocrResult.Value.FullText;
                }
            }

            if (cached.OcrResult != null)
            {
                OcrText = cached.EditedText ?? cached.OcrResult.FullText;
                Confidence = cached.OcrResult.Confidence / 100.0;
                WordCount = cached.OcrResult.Words.Count;
                LowConfidenceWordCount = cached.OcrResult.Words.Count(w => w.Confidence < 60);
            }

            StatusMessage = $"Page {zeroBasedIndex + 1} of {TotalPages}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading OCR page {Page}", zeroBasedIndex + 1);
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (CurrentPage >= TotalPages) return;
        SaveCurrentPageText();
        CurrentPage++;
        await LoadPageAsync(CurrentPage - 1);
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (CurrentPage <= 1) return;
        SaveCurrentPageText();
        CurrentPage--;
        await LoadPageAsync(CurrentPage - 1);
    }

    [RelayCommand]
    private void AcceptPage()
    {
        SaveCurrentPageText();
        _pageResults[CurrentPage - 1].IsAccepted = true;
        StatusMessage = $"Page {CurrentPage} accepted";
    }

    [RelayCommand]
    private void RejectPage()
    {
        _pageResults[CurrentPage - 1].IsRejected = true;
        StatusMessage = $"Page {CurrentPage} rejected";
    }

    [RelayCommand]
    private void AcceptAll()
    {
        SaveCurrentPageText();
        foreach (var r in _pageResults) r.IsAccepted = true;
        StatusMessage = "All pages accepted";
    }

    [RelayCommand]
    private async Task ReRunOcrAsync()
    {
        IsProcessing = true;
        _pageResults[CurrentPage - 1].OcrResult = null;
        await LoadPageAsync(CurrentPage - 1);
    }

    [RelayCommand]
    private void CopyText()
    {
        System.Windows.Clipboard.SetText(OcrText);
        StatusMessage = "Copied to clipboard";
    }

    [RelayCommand]
    private void ZoomInImage() => ImageZoomWidth = Math.Min(ImageZoomWidth * 1.2, 2400);

    [RelayCommand]
    private void ZoomOutImage() => ImageZoomWidth = Math.Max(ImageZoomWidth / 1.2, 200);

    [RelayCommand]
    private void FitImage() => ImageZoomWidth = 600;

    [RelayCommand]
    private void SaveAndClose()
    {
        SaveCurrentPageText();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SaveCurrentPageText()
    {
        if (CurrentPage < 1 || CurrentPage > _pageResults.Count) return;
        _pageResults[CurrentPage - 1].EditedText = OcrText;
    }

    private sealed class OcrPageResult
    {
        public int PageIndex { get; init; }
        public OcrResult? OcrResult { get; set; }
        public string? EditedText { get; set; }
        public bool IsAccepted { get; set; }
        public bool IsRejected { get; set; }
    }
}
