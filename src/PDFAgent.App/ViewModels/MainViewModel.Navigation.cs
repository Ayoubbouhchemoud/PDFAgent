using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace PDFAgent.App.ViewModels;

public sealed partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDocumentEmpty))]
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
    private bool _isDocumentLoaded;

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private string? _searchQuery;

    [ObservableProperty]
    private string _searchStatus = string.Empty;

    public bool IsDocumentEmpty => !IsDocumentLoaded;

    public event EventHandler? BatchWorkflowRequested;
    public event EventHandler? OcrReviewRequested;

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

    [RelayCommand]
    private void OpenSearch() => IsSearchVisible = true;

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchVisible = false;
        SearchQuery = null;
        SearchStatus = string.Empty;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (!IsDocumentLoaded || string.IsNullOrWhiteSpace(SearchQuery)) return;

        SearchStatus = "Searching…";
        var query = SearchQuery!;
        var matchPages = new List<int>();

        try
        {
            for (var i = 0; i < TotalPages; i++)
            {
                var textResult = await _pdfEngine.ExtractTextAsync(i);
                if (!textResult.IsSuccess || textResult.Value == null) continue;
                var fullText = string.Concat(textResult.Value.Select(s => s.Text));
                if (fullText.Contains(query, StringComparison.OrdinalIgnoreCase))
                    matchPages.Add(i + 1);
            }

            if (matchPages.Count > 0)
            {
                CurrentPage = matchPages[0];
                SearchStatus = $"{matchPages.Count} page(s) match";
                StatusText = $"Search: '{query}' found on {matchPages.Count} page(s) — showing page {matchPages[0]}";
            }
            else
            {
                SearchStatus = "No matches";
                StatusText = $"Search: no matches found for '{query}'";
            }
        }
        catch (Exception ex)
        {
            SearchStatus = "Search error";
            _logger.LogError(ex, "Search failed");
        }
    }

    partial void OnDocumentInfoChanged(Core.Models.PdfDocumentInfo? value)
    {
        IsDocumentLoaded = value is not null;
    }
}
