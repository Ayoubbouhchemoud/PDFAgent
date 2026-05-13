using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PDFAgent.Core.Models;

namespace PDFAgent.App.ViewModels;

public sealed partial class ViewerViewModel : ObservableObject
{
    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages;

    [ObservableProperty]
    private string? _searchQuery;

    [ObservableProperty]
    private bool _isSearchActive;

    [ObservableProperty]
    private bool _isThumbnailPanelVisible = true;

    [ObservableProperty]
    private bool _isSidePanelVisible = true;

    public ObservableCollection<PdfAnnotation> Annotations { get; } = new();
    public ObservableCollection<SearchResultItem> SearchResults { get; } = new();

    [RelayCommand]
    private void ZoomIn()
    {
        var idx = ZoomLevels.IndexOf(ZoomLevel);
        if (idx < ZoomLevels.Count - 1) ZoomLevel = ZoomLevels[idx + 1];
    }

    [RelayCommand]
    private void ZoomOut()
    {
        var idx = ZoomLevels.IndexOf(ZoomLevel);
        if (idx > 0) ZoomLevel = ZoomLevels[idx - 1];
    }

    [RelayCommand]
    private void FitToWidth() => ZoomLevel = 1.0;

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
    private void ToggleThumbnails() => IsThumbnailPanelVisible = !IsThumbnailPanelVisible;

    [RelayCommand]
    private void ToggleSidePanel() => IsSidePanelVisible = !IsSidePanelVisible;

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        IsSearchActive = true;
        SearchResults.Clear();
        // Search implementation will go here
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = null;
        IsSearchActive = false;
        SearchResults.Clear();
    }

    private static readonly List<double> ZoomLevels = new()
        { 0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 2.0, 3.0, 4.0, 5.0, 8.0, 10.0 };
}

public sealed class SearchResultItem
{
    public int PageNumber { get; set; }
    public string ContextText { get; set; } = string.Empty;
    public int MatchIndex { get; set; }
}
