using System.Windows;
using System.Windows.Media;
using PDFAgent.App.ViewModels;
using PDFAgent.Core.Models;

namespace PDFAgent.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private readonly BatchWorkflowViewModel _batchVm;
    private readonly OcrReviewViewModel _ocrVm;

    public MainWindow(
        MainViewModel mainViewModel,
        BatchWorkflowViewModel batchVm,
        OcrReviewViewModel ocrVm)
    {
        InitializeComponent();
        _mainViewModel = mainViewModel;
        _batchVm = batchVm;
        _ocrVm = ocrVm;

        DataContext = mainViewModel;

        mainViewModel.BatchWorkflowRequested += (_, _) => OpenBatchWorkflow();
        mainViewModel.OcrReviewRequested    += (_, _) => OpenOcrReview();
        mainViewModel.SignRequested         += async (_, _) => await OpenSignatureDialogAsync();

        Loaded += async (_, _) => await mainViewModel.InitializeAsync();
    }

    private void OpenBatchWorkflow()
    {
        var view = new BatchWorkflowView { DataContext = _batchVm };
        var win = new Window
        {
            Title = "Batch Workflow Editor",
            Width = 980,
            Height = 660,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = TryFindResource("SurfaceBrush") as Brush
                         ?? SystemColors.WindowBrush,
            Content = view,
            ResizeMode = ResizeMode.CanResizeWithGrip,
        };
        win.ShowDialog();
    }

    private async Task OpenSignatureDialogAsync()
    {
        var vm = new SignatureDialogViewModel
        {
            TargetPage = _mainViewModel.CurrentPage,
            TotalPages = _mainViewModel.TotalPages,
        };

        var dialog = new SignatureDialog { DataContext = vm, Owner = this };
        if (dialog.ShowDialog() != true || vm.SignatureBytes == null) return;

        var opts = new SignatureOverlayOptions
        {
            ImageBytes      = vm.SignatureBytes,
            PageNumber      = vm.TargetPage,
            Placement       = vm.Placement,
            SignatureWidth  = 200,
            SignatureHeight = 80,
        };
        await _mainViewModel.ApplySignatureAsync(opts);
    }

    private void OpenOcrReview()
    {
        if (!_mainViewModel.IsDocumentLoaded)
        {
            MessageBox.Show("Open a PDF document first.", "OCR Review",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var view = new OcrReviewView { DataContext = _ocrVm };
        var win = new Window
        {
            Title = "OCR Review",
            Width = 1100,
            Height = 720,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = TryFindResource("SurfaceBrush") as Brush
                         ?? SystemColors.WindowBrush,
            Content = view,
            ResizeMode = ResizeMode.CanResizeWithGrip,
        };

        _ocrVm.CloseRequested += (_, _) => win.Close();
        win.Loaded += async (_, _) => await _ocrVm.LoadAsync();
        win.ShowDialog();
    }
}
