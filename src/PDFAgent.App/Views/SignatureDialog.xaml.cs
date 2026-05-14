using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PDFAgent.App.ViewModels;

namespace PDFAgent.App.Views;

public partial class SignatureDialog : Window
{
    private bool _isDrawing;
    private System.Windows.Shapes.Polyline? _currentPolyline;

    public SignatureDialog()
    {
        InitializeComponent();
    }

    private SignatureDialogViewModel Vm => (SignatureDialogViewModel)DataContext;

    // ── Drawing ──────────────────────────────────────────────────────────────

    private void DrawCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        _isDrawing = true;
        DrawHint.Visibility = Visibility.Collapsed;

        var pos = e.GetPosition(DrawCanvas);
        _currentPolyline = new System.Windows.Shapes.Polyline
        {
            Stroke             = Brushes.Black,
            StrokeThickness    = 2.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
            StrokeLineJoin     = PenLineJoin.Round,
            Points             = new PointCollection { pos, pos },
        };
        DrawCanvas.Children.Add(_currentPolyline);
        DrawCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void DrawCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing || _currentPolyline == null) return;
        _currentPolyline.Points.Add(e.GetPosition(DrawCanvas));
        e.Handled = true;
    }

    private void DrawCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDrawing = false;
        _currentPolyline = null;
        DrawCanvas.ReleaseMouseCapture();
    }

    private void DrawCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        _currentPolyline = null;
        DrawCanvas.ReleaseMouseCapture();
    }

    private void ClearDrawing_Click(object sender, MouseButtonEventArgs e)
    {
        DrawCanvas.Children.Clear();
        DrawHint.Visibility = Visibility.Visible;
    }

    // Serialise all polylines to compact vector JSON that the PDF engine re-draws.
    private byte[] ExtractDrawingAsVectorJson()
    {
        DrawCanvas.UpdateLayout();
        var canvasW = Math.Max(DrawCanvas.ActualWidth,  1);
        var canvasH = Math.Max(DrawCanvas.ActualHeight, 1);

        var strokes = DrawCanvas.Children
            .OfType<System.Windows.Shapes.Polyline>()
            .Select(pl => pl.Points.Select(pt => new[] { pt.X, pt.Y }).ToArray())
            .Where(s => s.Length >= 2)
            .ToArray();

        var payload = new { t = "v", w = canvasW, h = canvasH, s = strokes };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
    }

    // ── Image upload ──────────────────────────────────────────────────────────

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select signature image",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var bmp = new BitmapImage(new Uri(dlg.FileName));
            bmp.Freeze();
            Vm.RawUploadedBitmap = bmp;
            Vm.UploadStatus      = Path.GetFileName(dlg.FileName);
            RefreshUploadedPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load image: {ex.Message}", "Sign",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Vm?.RawUploadedBitmap != null) RefreshUploadedPreview();
    }

    private void RefreshUploadedPreview()
    {
        if (Vm.RawUploadedBitmap == null) return;
        var bytes = ApplyThreshold(Vm.RawUploadedBitmap, (byte)Vm.Threshold);
        Vm.SignatureBytes = bytes;

        using var ms = new MemoryStream(bytes);
        var preview  = new BitmapImage();
        preview.BeginInit();
        preview.StreamSource = ms;
        preview.CacheOption  = BitmapCacheOption.OnLoad;
        preview.EndInit();
        preview.Freeze();
        Vm.UploadedPreview = preview;
    }

    // Pixels where R, G, B are all >= threshold → transparent.
    private static byte[] ApplyThreshold(BitmapSource source, byte threshold)
    {
        var bgra   = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width  = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 2] >= threshold  // R
             && pixels[i + 1] >= threshold  // G
             && pixels[i + 0] >= threshold) // B
                pixels[i + 3] = 0;
        }

        var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

        using var ms  = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(wb));
        enc.Save(ms);
        return ms.ToArray();
    }

    // ── Apply / Cancel ────────────────────────────────────────────────────────

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.IsDrawTab)
        {
            if (DrawCanvas.Children.Count == 0)
            {
                MessageBox.Show("Please draw your signature first.",
                    "Create Signature", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Vm.SignatureBytes = ExtractDrawingAsVectorJson();
        }
        else
        {
            if (Vm.SignatureBytes == null)
            {
                MessageBox.Show("Please select a signature image first.",
                    "Create Signature", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
