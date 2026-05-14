using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using PDFAgent.App.ViewModels;

namespace PDFAgent.App.Views;

public partial class SignatureDialog : Window
{
    private bool _isDrawing;
    private Polyline? _currentPolyline;

    private static readonly Brush StrokeBrush = Brushes.Black;
    private const double StrokeThickness = 2.0;

    public SignatureDialog() => InitializeComponent();

    private SignatureDialogViewModel Vm => (SignatureDialogViewModel)DataContext;

    // ── Drawing ─────────────────────────────────────────────────────────────

    private void DrawCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        _isDrawing = true;
        DrawHint.Visibility = Visibility.Collapsed;

        var pos = e.GetPosition(DrawCanvas);
        _currentPolyline = new Polyline
        {
            Stroke = StrokeBrush,
            StrokeThickness = StrokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Points = new PointCollection { pos, pos }, // two identical pts → visible dot on click
        };
        DrawCanvas.Children.Add(_currentPolyline);
        DrawCanvas.CaptureMouse();
    }

    private void DrawCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing || _currentPolyline == null) return;
        _currentPolyline.Points.Add(e.GetPosition(DrawCanvas));
    }

    private void DrawCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDrawing = false;
        _currentPolyline = null;
        DrawCanvas.ReleaseMouseCapture();
    }

    private void DrawCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDrawing)
        {
            _isDrawing = false;
            _currentPolyline = null;
            DrawCanvas.ReleaseMouseCapture();
        }
    }

    private void ClearDrawing_Click(object sender, RoutedEventArgs e)
    {
        DrawCanvas.Children.Clear();
        DrawHint.Visibility = Visibility.Visible;
    }

    // ── Image upload ─────────────────────────────────────────────────────────

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select signature image",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var bmp = new BitmapImage(new Uri(dlg.FileName));
            bmp.Freeze();
            Vm.RawUploadedBitmap = bmp;
            Vm.UploadStatus = System.IO.Path.GetFileName(dlg.FileName);
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
        var preview = new BitmapImage();
        preview.BeginInit();
        preview.StreamSource = ms;
        preview.CacheOption = BitmapCacheOption.OnLoad;
        preview.EndInit();
        preview.Freeze();
        Vm.UploadedPreview = preview;
    }

    // ── Rendering helpers ─────────────────────────────────────────────────────

    private BitmapSource RenderDrawingToBitmap()
    {
        var w = (int)DrawCanvas.ActualWidth;
        var h = (int)DrawCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(DrawCanvas) { Stretch = Stretch.None },
                null, new Rect(0, 0, w, h));
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // Pixels brighter than `threshold` in all channels become transparent.
    private static byte[] ApplyThreshold(BitmapSource source, byte threshold)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width  = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i + 0];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            if (r >= threshold && g >= threshold && b >= threshold)
                pixels[i + 3] = 0; // transparent
        }

        var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

        using var ms = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(wb));
        enc.Save(ms);
        return ms.ToArray();
    }

    // ── Apply / Cancel ───────────────────────────────────────────────────────

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.IsDrawTab)
        {
            if (DrawCanvas.Children.Count == 0)
            {
                MessageBox.Show("Please draw your signature first.",
                    "Sign Document", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var bitmap = RenderDrawingToBitmap();
            Vm.SignatureBytes = ApplyThreshold(bitmap, (byte)Vm.Threshold);
        }
        else
        {
            if (Vm.SignatureBytes == null)
            {
                MessageBox.Show("Please select a signature image first.",
                    "Sign Document", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
