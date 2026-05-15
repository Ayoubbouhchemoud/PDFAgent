using System.Windows;
using System.Windows.Media;
using PDFAgent.App.ViewModels;
using PDFAgent.Core.Models;

namespace PDFAgent.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel          _mainViewModel;
    private readonly BatchWorkflowViewModel _batchVm;
    private readonly OcrReviewViewModel     _ocrVm;

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

        // After any in-place PDF modification (sign, rotate, redact…), reload the viewer
        // so WebView2 shows the updated file rather than the cached previous version.
        mainViewModel.ViewerRefreshRequested += () => PdfWebView.Reload();

        mainViewModel.PropertyChanged += async (_, pe) =>
        {
            switch (pe.PropertyName)
            {
                // New file opened → navigate WebView2 to the real PDF.
                case nameof(MainViewModel.OpenedFilePath):
                    NavigateToPdf(mainViewModel.OpenedFilePath);
                    break;

                // Zoom slider/buttons → WebView2 zoom factor.
                case nameof(MainViewModel.CurrentZoom):
                    if (PdfWebView.CoreWebView2 != null)
                        PdfWebView.ZoomFactor = mainViewModel.CurrentZoom;
                    break;

                // Search/page-navigation buttons → scroll PDF viewer to the right page.
                case nameof(MainViewModel.CurrentPage):
                    await NavigateToPageAsync(mainViewModel.CurrentPage);
                    break;
            }
        };

        Loaded += OnWindowLoaded;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        await PdfWebView.EnsureCoreWebView2Async(null);

        PdfWebView.CoreWebView2.Settings.IsStatusBarEnabled              = false;
        // Keep browser accelerator keys enabled so Ctrl+F / Ctrl+P work inside the PDF viewer.
        PdfWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
        PdfWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled   = true;

        await _mainViewModel.InitializeAsync();
    }

    private void NavigateToPdf(string? filePath)
    {
        if (PdfWebView.CoreWebView2 == null) return;

        if (string.IsNullOrEmpty(filePath))
        {
            PdfWebView.CoreWebView2.Navigate("about:blank");
            return;
        }

        var uri = new Uri(filePath).AbsoluteUri;
        PdfWebView.CoreWebView2.Navigate(uri);
        PdfWebView.ZoomFactor = _mainViewModel.CurrentZoom;
    }

    private async Task NavigateToPageAsync(int page)
    {
        if (PdfWebView.CoreWebView2 == null || !_mainViewModel.IsDocumentLoaded) return;
        // Change the URL fragment — Chromium treats a same-resource fragment change as a
        // same-document navigation (no reload).  Chrome's PDF viewer listens to hashchange
        // and scrolls to the requested page.
        await PdfWebView.CoreWebView2.ExecuteScriptAsync(
            $"window.location.hash = 'page={page}'");
    }

    // ── Search bar focus ──────────────────────────────────────────────────────

    private void SearchBorder_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SearchQueryTextBox.Focus();
    }

    // ── Sign: show signature dialog, then placement dialog, then burn ─────────

    private async Task OpenSignatureDialogAsync()
    {
        // Step 1 — get the signature image.
        var sigVm  = new SignatureDialogViewModel();
        var sigDlg = new SignatureDialog { DataContext = sigVm, Owner = this };
        if (sigDlg.ShowDialog() != true || sigVm.SignatureBytes == null) return;

        // Step 2 — let the user choose page + position.
        var placeDlg = new SignaturePlacementDialog(
            _mainViewModel.TotalPages,
            _mainViewModel.CurrentPage)
        {
            Owner = this,
        };
        if (placeDlg.ShowDialog() != true) return;

        // Step 3 — burn signature directly into the PDF (no overlay sticker needed).
        // ApplySignatureAsync handles the file write, engine reopen, and fires
        // ViewerRefreshRequested so WebView2 reloads the updated document.
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
