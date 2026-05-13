using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PDFAgent.App.ViewModels;

// Navigation, search, zoom and view-toggle commands surfaced to MainWindow.
public sealed partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDocumentEmpty))]
    private bool _isDocumentLoaded;

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private string? _searchQuery;

    public bool IsDocumentEmpty => !IsDocumentLoaded;

    // Fired when the host should open the BatchWorkflow panel/window.
    public event EventHandler? BatchWorkflowRequested;

    // Fired when the host should open the OCR Review panel/window.
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
    }

    partial void OnDocumentInfoChanged(Core.Models.PdfDocumentInfo? value)
    {
        IsDocumentLoaded = value is not null;
    }
}
