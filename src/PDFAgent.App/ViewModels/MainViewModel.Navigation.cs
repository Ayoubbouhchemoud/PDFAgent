using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PDFAgent.Core.Models;

namespace PDFAgent.App.ViewModels;

public sealed partial class MainViewModel
{
    // Path of the currently open file — the main view navigates WebView2 to this URI.
    [ObservableProperty]
    private string? _openedFilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDocumentEmpty))]
    [NotifyCanExecuteChangedFor(nameof(SaveFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(SplitCommand))]
    [NotifyCanExecuteChangedFor(nameof(RotateCommand))]
    [NotifyCanExecuteChangedFor(nameof(OcrCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedactCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnnotateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyTextAnnotationsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportToImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    [NotifyCanExecuteChangedFor(nameof(PropertiesCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleTextEditModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyTextEditsCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPageCommand))]
    private bool _isDocumentLoaded;

    // ── Search state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private string? _searchQuery;

    [ObservableProperty]
    private string _searchStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchCurrentPosition))]
    [NotifyCanExecuteChangedFor(nameof(SearchNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchPrevCommand))]
    private int _searchHitCount;

    // 0-based page index for each phrase-match occurrence (navigation list)
    private readonly List<int> _searchHitPageIndices = new();
    private int _currentHitIndex = -1;

    /// <summary>"3 / 15" counter shown next to the search strip navigation buttons.</summary>
    public string SearchCurrentPosition =>
        SearchHitCount == 0 ? string.Empty : $"{_currentHitIndex + 1} / {SearchHitCount}";

    // ── Navigation helpers ────────────────────────────────────────────────────

    public bool IsDocumentEmpty => !IsDocumentLoaded;

    public event EventHandler? BatchWorkflowRequested;
    public event EventHandler? OcrReviewRequested;
    public event EventHandler? SearchFocusRequested;

    [RelayCommand]
    private void OpenBatchWorkflow() => BatchWorkflowRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenOcrReview() => OcrReviewRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ZoomIn()
    {
        var idx = ZoomLevels.IndexOf(CurrentZoom);
        if (idx < ZoomLevels.Count - 1) CurrentZoom = ZoomLevels[idx + 1];
    }

    [RelayCommand]
    private void ZoomOut()
    {
        var idx = ZoomLevels.IndexOf(CurrentZoom);
        if (idx > 0) CurrentZoom = ZoomLevels[idx - 1];
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage < TotalPages) CurrentPage++;
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1) CurrentPage--;
    }

    // ── Search commands ───────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenSearch()
    {
        IsSearchVisible = true;
        SearchFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchVisible = false;
        SearchQuery     = null;
        SearchStatus    = string.Empty;
        SearchHitCount  = 0;
        ClearSearchState();
        foreach (var page in RenderedPages) page.SearchHighlights.Clear();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (!IsDocumentLoaded || string.IsNullOrWhiteSpace(SearchQuery)) return;

        ClearSearchState();
        SearchHitCount  = 0;
        IsSearchVisible = true;
        SearchStatus    = "Searching…";

        var query    = SearchQuery!.Trim();
        var queryLow = query.ToLowerInvariant();

        try
        {
            var pagesWithText = 0;

            for (var pageIdx = 0; pageIdx < TotalPages; pageIdx++)
            {
                // Use GetPageTextAsync which reads the already-open PdfiumViewer document handle.
                // This is guaranteed to work for text-based PDFs — no P/Invoke file re-open needed.
                var textResult = await _pdfEngine.GetPageTextAsync(pageIdx);
                if (!textResult.IsSuccess || textResult.Value == null) continue;

                var pageText = textResult.Value;
                if (string.IsNullOrWhiteSpace(pageText)) continue;

                pagesWithText++;

                if (pageText.ToLowerInvariant().Contains(queryLow))
                    _searchHitPageIndices.Add(pageIdx);
            }

            // Deduplicate (each page appears at most once in the navigation list)
            var uniquePages = _searchHitPageIndices.Distinct().OrderBy(p => p).ToList();
            _searchHitPageIndices.Clear();
            _searchHitPageIndices.AddRange(uniquePages);

            SearchHitCount = _searchHitPageIndices.Count;

            if (SearchHitCount == 0)
            {
                SearchStatus = pagesWithText == 0
                    ? "No text found — document appears to be scanned. Run OCR to enable search."
                    : $"No results for \"{query}\"";
            }
            else
            {
                _currentHitIndex = 0;
                CurrentPage      = _searchHitPageIndices[0] + 1;
                SearchStatus     = SearchHitCount == 1
                    ? "Found on 1 page"
                    : $"Found on {SearchHitCount} pages";
                OnPropertyChanged(nameof(SearchCurrentPosition));
            }
        }
        catch (Exception ex)
        {
            SearchStatus = "Search error";
            _logger.LogError(ex, "Search failed");
        }
    }

    private bool CanSearchNext() => SearchHitCount > 0;

    [RelayCommand(CanExecute = nameof(CanSearchNext))]
    private void SearchNext()
    {
        if (_searchHitPageIndices.Count == 0) return;
        _currentHitIndex = (_currentHitIndex + 1) % _searchHitPageIndices.Count;
        CurrentPage      = _searchHitPageIndices[_currentHitIndex] + 1;
        OnPropertyChanged(nameof(SearchCurrentPosition));
    }

    private bool CanSearchPrev() => SearchHitCount > 0;

    [RelayCommand(CanExecute = nameof(CanSearchPrev))]
    private void SearchPrev()
    {
        if (_searchHitPageIndices.Count == 0) return;
        _currentHitIndex = ((_currentHitIndex - 1) + _searchHitPageIndices.Count) % _searchHitPageIndices.Count;
        CurrentPage      = _searchHitPageIndices[_currentHitIndex] + 1;
        OnPropertyChanged(nameof(SearchCurrentPosition));
    }

    private void ClearSearchState()
    {
        _searchHitPageIndices.Clear();
        _currentHitIndex = -1;
        OnPropertyChanged(nameof(SearchCurrentPosition));
    }

    partial void OnDocumentInfoChanged(Core.Models.PdfDocumentInfo? value)
    {
        IsDocumentLoaded = value is not null;
    }
}

// ── Search highlight data class ───────────────────────────────────────────────

/// <summary>PDF-point coordinates for one search match. PdfPageView converts to canvas DIPs.</summary>
public sealed class SearchHighlightRect
{
    public double X      { get; init; }
    public double Y      { get; init; }
    public double Width  { get; init; }
    public double Height { get; init; }
}
