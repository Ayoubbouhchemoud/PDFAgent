using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using PDFAgent.App.ViewModels;
using PDFAgent.Core.Models;

namespace PDFAgent.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel          _mainViewModel;
    private readonly BatchWorkflowViewModel _batchVm;
    private readonly OcrReviewViewModel     _ocrVm;
    private string? _currentPdfPath;

    public MainWindow(
        MainViewModel mainViewModel,
        BatchWorkflowViewModel batchVm,
        OcrReviewViewModel ocrVm)
    {
        InitializeComponent();
        _mainViewModel = mainViewModel;
        _batchVm       = batchVm;
        _ocrVm         = ocrVm;

        DataContext = mainViewModel;

        mainViewModel.BatchWorkflowRequested += (_, _) => OpenBatchWorkflow();
        mainViewModel.OcrReviewRequested     += (_, _) => OpenOcrReview();
        mainViewModel.SignRequested          += (_, _) => _ = OpenSignatureDialogAsync();
        mainViewModel.ZoomPageRequested      += pageNum => _ = ShowPageZoomAsync(pageNum);
        mainViewModel.SearchFocusRequested   += (_, _) =>
        {
            SearchQueryTextBox.Focus();
            SearchQueryTextBox.SelectAll();
        };

        // After any in-place edit (sign, rotate, redact…) re-copy and reload so the
        // viewer shows the updated bytes — the cached temp copy would be stale otherwise.
        mainViewModel.ViewerRefreshRequested += () =>
            Dispatcher.Invoke(() => NavigateToPdf(_currentPdfPath));

        mainViewModel.PropertyChanged += async (_, pe) =>
        {
            switch (pe.PropertyName)
            {
                case nameof(MainViewModel.OpenedFilePath):
                    NavigateToPdf(mainViewModel.OpenedFilePath);
                    break;

                case nameof(MainViewModel.CurrentZoom):
                    if (PdfWebView.CoreWebView2 != null)
                        PdfWebView.ZoomFactor = mainViewModel.CurrentZoom;
                    break;

                case nameof(MainViewModel.CurrentPage):
                    await NavigateToPageAsync(mainViewModel.CurrentPage);
                    break;

                case nameof(MainViewModel.SearchHitCount):
                    var q = mainViewModel.SearchQuery?.Trim();
                    if (mainViewModel.SearchHitCount > 0 && !string.IsNullOrEmpty(q))
                        await TriggerPdfJsFindAsync(q);
                    break;

                case nameof(MainViewModel.IsSearchVisible):
                    if (!mainViewModel.IsSearchVisible)
                        await ClearPdfJsHighlightsAsync();
                    break;
            }
        };

        Loaded += OnWindowLoaded;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        await PdfWebView.EnsureCoreWebView2Async(null);

        // Serve the entire app output folder via HTTPS so PDF.js and the temp PDF
        // are both on the same origin (https://pdfapp) — no CORS, no fetch restrictions.
        PdfWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "pdfapp",
            AppContext.BaseDirectory,
            CoreWebView2HostResourceAccessKind.Allow);

        PdfWebView.CoreWebView2.Settings.IsStatusBarEnabled               = false;
        PdfWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
        PdfWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled    = true;

        PdfWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

        await _mainViewModel.InitializeAsync();
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        // Hide PDF.js toolbar so the viewer fills the whole WebView2 area.
        await PdfWebView.CoreWebView2.ExecuteScriptAsync("""
            (function() {
                var s = document.getElementById('__pdfagent_style');
                if (!s) { s = document.createElement('style'); s.id='__pdfagent_style'; document.head.appendChild(s); }
                s.textContent =
                    '#toolbarViewer,#secondaryToolbar{display:none!important}' +
                    '#viewerContainer{top:0!important}';
            })();
            """);
    }

    private void NavigateToPdf(string? filePath)
    {
        if (PdfWebView.CoreWebView2 == null) return;

        _currentPdfPath = filePath;

        if (string.IsNullOrEmpty(filePath))
        {
            PdfWebView.CoreWebView2.Navigate("about:blank");
            return;
        }

        // Copy the PDF into pdftemp/ under the app's base directory.
        // Because pdfapp maps to AppContext.BaseDirectory, the copied file is served at
        // https://pdfapp/pdftemp/{name} — same origin as the PDF.js viewer, so no CORS.
        var tempDir = Path.Combine(AppContext.BaseDirectory, "pdftemp");
        Directory.CreateDirectory(tempDir);
        var fileName = Path.GetFileName(filePath);
        var destPath = Path.Combine(tempDir, fileName);

        try { File.Copy(filePath, destPath, overwrite: true); }
        catch { /* best-effort; viewer will show its own error if the fetch fails */ }

        var encodedName = Uri.EscapeDataString(fileName);
        // Append a timestamp fragment so repeated navigations (after edits) bypass the cache.
        var ts         = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var viewerUrl  = $"https://pdfapp/pdfjs/web/viewer.html?file=https://pdfapp/pdftemp/{encodedName}&ts={ts}";
        PdfWebView.CoreWebView2.Navigate(viewerUrl);
        PdfWebView.ZoomFactor = _mainViewModel.CurrentZoom;
    }

    private async Task NavigateToPageAsync(int page)
    {
        if (PdfWebView.CoreWebView2 == null || !_mainViewModel.IsDocumentLoaded) return;
        await PdfWebView.CoreWebView2.ExecuteScriptAsync(
            $"if(window.PDFViewerApplication) PDFViewerApplication.page = {page};");
    }

    // ── Search bar focus ──────────────────────────────────────────────────────

    private void SearchBorder_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SearchQueryTextBox.Focus();
    }

    // ── PDF.js word-level highlights ──────────────────────────────────────────

    private async Task TriggerPdfJsFindAsync(string query)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(query);

        await PdfWebView.CoreWebView2.ExecuteScriptAsync($$"""
            (async () => {
                for (let i = 0; i < 50; i++) {
                    if (window.PDFViewerApplication && PDFViewerApplication.eventBus) break;
                    await new Promise(r => setTimeout(r, 100));
                }
                if (!window.PDFViewerApplication) return;
                await PDFViewerApplication.initializedPromise;
                PDFViewerApplication.eventBus.dispatch('find', {
                    source:        window,
                    type:          '',
                    query:         {{json}},
                    caseSensitive: false,
                    entireWord:    false,
                    highlightAll:  true,
                    findPrevious:  false
                });
            })();
            """);
    }

    private async Task ClearPdfJsHighlightsAsync()
    {
        if (PdfWebView.CoreWebView2 == null) return;
        await PdfWebView.CoreWebView2.ExecuteScriptAsync("""
            if (window.PDFViewerApplication && PDFViewerApplication.eventBus)
                PDFViewerApplication.eventBus.dispatch('find', {
                    source: window, type: '', query: '',
                    caseSensitive: false, entireWord: false,
                    highlightAll: false, findPrevious: false
                });
            """);
    }

    // ── Sign ──────────────────────────────────────────────────────────────────

    private async Task OpenSignatureDialogAsync()
    {
        var sigVm  = new SignatureDialogViewModel();
        var sigDlg = new SignatureDialog { DataContext = sigVm, Owner = this };
        if (sigDlg.ShowDialog() != true || sigVm.SignatureBytes == null) return;

        var placeDlg = new SignaturePlacementDialog(
            _mainViewModel.TotalPages,
            _mainViewModel.CurrentPage)
        {
            Owner = this,
        };
        if (placeDlg.ShowDialog() != true) return;

        await _mainViewModel.PlaceSignatureAsync(
            sigVm.SignatureBytes,
            placeDlg.ChosenPage,
            placeDlg.ChosenPlacement);
    }

    // ── Batch workflow ────────────────────────────────────────────────────────

    private void OpenBatchWorkflow()
    {
        var view = new BatchWorkflowView { DataContext = _batchVm };
        var win  = new Window
        {
            Title                 = "Batch Workflow Editor",
            Width                 = 980,
            Height                = 660,
            Owner                 = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background            = TryFindResource("SurfaceBrush") as Brush ?? SystemColors.WindowBrush,
            Content               = view,
            ResizeMode            = ResizeMode.CanResizeWithGrip,
        };
        win.ShowDialog();
    }

    // ── Split page zoom ───────────────────────────────────────────────────────

    private async Task ShowPageZoomAsync(int pageNumber)
    {
        var bytes = await _mainViewModel.RenderPageForZoomAsync(pageNumber - 1);
        if (bytes == null) return;

        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new System.IO.MemoryStream(bytes);
        bmp.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();

        var scroll = new System.Windows.Controls.ScrollViewer
        {
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = System.Windows.Controls.ScrollBarVisibility.Auto,
            Background = System.Windows.Media.Brushes.DimGray,
            Content    = new System.Windows.Controls.Image
            {
                Source  = bmp,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Margin  = new System.Windows.Thickness(12),
            },
        };

        new Window
        {
            Title                 = $"Page {pageNumber} — Preview",
            Width                 = 720,
            Height                = 880,
            Owner                 = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background            = System.Windows.Media.Brushes.DimGray,
            Content               = scroll,
            ResizeMode            = ResizeMode.CanResizeWithGrip,
        }.Show();
    }

    // ── OCR review ────────────────────────────────────────────────────────────

    private void OpenOcrReview()
    {
        if (!_mainViewModel.IsDocumentLoaded)
        {
            MessageBox.Show("Open a PDF document first.", "OCR Review",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var view = new OcrReviewView { DataContext = _ocrVm };
        var win  = new Window
        {
            Title                 = "OCR Review",
            Width                 = 1100,
            Height                = 720,
            Owner                 = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background            = TryFindResource("SurfaceBrush") as Brush ?? SystemColors.WindowBrush,
            Content               = view,
            ResizeMode            = ResizeMode.CanResizeWithGrip,
        };

        _ocrVm.CloseRequested += (_, _) => win.Close();
        win.Loaded += async (_, _) => await _ocrVm.LoadAsync();
        win.ShowDialog();
    }
}
