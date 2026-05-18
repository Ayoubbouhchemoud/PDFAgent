using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;

namespace PDFAgent.App.Views;

public partial class DrawingDialog : Window
{
    // PDF pts → WPF DIPs: canvas_coord = pdf_coord * Scale
    // PDF pts ← WPF DIPs: pdf_coord   = canvas_coord / Scale
    private const double Scale     = 96.0 / 72.0;
    private const double RenderDpi = 150.0;

    // Eraser: stroke is removed if any of its points falls within this radius (canvas DIPs)
    private const double EraserRadius = 18.0;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly IPdfEngine _engine;
    private readonly int        _totalPages;
    private int                 _currentPageIdx;

    private enum DrawTool { Pen, Highlight, Erase }
    private DrawTool _tool = DrawTool.Pen;

    private Color  _penColor     = Colors.Black;
    private double _penThickness = 2.0;   // PDF pts (Pen)
    private Color  _hlColor      = Color.FromRgb(255, 230, 0);
    private double _hlThickness  = 10.0;  // PDF pts (Highlighter)
    private double _hlOpacity    = 0.40;

    // Strokes per page: page-idx → list of (Polyline on canvas, DrawingStroke data)
    private readonly Dictionary<int, List<(Polyline Visual, DrawingStroke Data)>> _strokesByPage = new();

    // Active stroke being drawn
    private Polyline?      _activePolyline;
    private DrawingStroke? _activeStroke;
    private bool           _isDrawing;

    // Eraser cursor ring
    private Ellipse? _eraserRing;

    // Color and thickness button references for selection highlighting
    private readonly List<(Border Frame, Color Color, bool IsHl)> _colorSwatches = new();
    private readonly List<(Border Frame, double Thickness)>        _thickButtons  = new();
    private Border? _selectedColorFrame;
    private Border? _selectedThickFrame;

    public IReadOnlyList<DrawingStroke> CollectedStrokes { get; private set; } =
        Array.Empty<DrawingStroke>();

    // ── Constructor ───────────────────────────────────────────────────────────

    public DrawingDialog(IPdfEngine engine, int totalPages)
    {
        _engine     = engine;
        _totalPages = totalPages;
        InitializeComponent();
        BuildColorPalette();
        BuildThicknessPalette();
        KeyDown += OnKeyDown;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private async void Window_Loaded(object sender, RoutedEventArgs e)
        => await LoadPageAsync(0);

    // ── Toolbar construction ──────────────────────────────────────────────────

    private static readonly (Color Color, bool IsHighlighter)[] Swatches =
    {
        (Colors.Black,             false),
        (Colors.DimGray,           false),
        (Color.FromRgb(220,  30,  30), false),
        (Color.FromRgb( 30, 110, 200), false),
        (Color.FromRgb( 20, 160,  70), false),
        (Color.FromRgb(200, 100,   0), false),
        (Color.FromRgb(130,  30, 180), false),
        (Color.FromRgb(255, 230,   0), true),  // default highlighter colour
    };

    private void BuildColorPalette()
    {
        foreach (var (color, isHl) in Swatches)
        {
            var outer = new Border
            {
                Width         = 26,
                Height        = 26,
                CornerRadius  = new CornerRadius(4),
                BorderThickness = new Thickness(2),
                BorderBrush   = Brushes.Transparent,
                Margin        = new Thickness(0, 0, 4, 0),
                Cursor        = Cursors.Hand,
            };
            var inner = new Border
            {
                Background   = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(2),
            };
            outer.Child = inner;

            var captured = (color, isHl);
            outer.MouseLeftButtonDown += (_, _) => SelectColor(outer, captured.color, captured.isHl);

            ColorPalette.Items.Add(outer);
            _colorSwatches.Add((outer, color, isHl));
        }

        // Pre-select black
        SelectColor(_colorSwatches[0].Frame, _colorSwatches[0].Color, false);
    }

    private static readonly (string Label, double PdfPts)[] ThicknessChoices =
    {
        ("·",  1.5),
        ("–",  3.0),
        ("—",  6.0),
        ("━", 12.0),
    };

    private void BuildThicknessPalette()
    {
        foreach (var (label, pts) in ThicknessChoices)
        {
            var outer = new Border
            {
                Width           = 36,
                Height          = 26,
                CornerRadius    = new CornerRadius(4),
                BorderThickness = new Thickness(2),
                BorderBrush     = Brushes.Transparent,
                Margin          = new Thickness(0, 0, 4, 0),
                Cursor          = Cursors.Hand,
                Child           = new TextBlock
                {
                    Text                = label,
                    FontSize            = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                    Foreground          = new SolidColorBrush(Colors.DimGray),
                },
            };

            var captured = pts;
            outer.MouseLeftButtonDown += (_, _) => SelectThickness(outer, captured);

            ThicknessPalette.Items.Add(outer);
            _thickButtons.Add((outer, pts));
        }

        // Pre-select 3pt
        SelectThickness(_thickButtons[1].Frame, _thickButtons[1].Thickness);
    }

    private void SelectColor(Border frame, Color color, bool isHighlightSwatch)
    {
        if (_selectedColorFrame != null)
            _selectedColorFrame.BorderBrush = Brushes.Transparent;
        frame.BorderBrush    = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        _selectedColorFrame  = frame;

        if (_tool == DrawTool.Highlight || isHighlightSwatch)
            _hlColor = color;
        else
            _penColor = color;
    }

    private void SelectThickness(Border frame, double pts)
    {
        if (_selectedThickFrame != null)
            _selectedThickFrame.BorderBrush = Brushes.Transparent;
        frame.BorderBrush   = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        _selectedThickFrame = frame;

        if (_tool == DrawTool.Highlight)
            _hlThickness = pts;
        else
            _penThickness = pts;
    }

    // ── Tool selection ────────────────────────────────────────────────────────

    private void PenBtn_Click(object sender, RoutedEventArgs e)        => SetTool(DrawTool.Pen);
    private void HighlightBtn_Click(object sender, RoutedEventArgs e)  => SetTool(DrawTool.Highlight);
    private void EraserBtn_Click(object sender, RoutedEventArgs e)     => SetTool(DrawTool.Erase);

    private void SetTool(DrawTool tool)
    {
        _tool = tool;

        var primaryBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        var transparentBrush = Brushes.Transparent;

        PenBtnBorder.BorderBrush       = tool == DrawTool.Pen       ? primaryBrush : transparentBrush;
        HighlightBtnBorder.BorderBrush = tool == DrawTool.Highlight  ? primaryBrush : transparentBrush;
        EraserBtnBorder.BorderBrush    = tool == DrawTool.Erase      ? primaryBrush : transparentBrush;

        DrawCanvas.Cursor = tool == DrawTool.Erase ? Cursors.No : Cursors.Cross;

        ShowOrHideEraserRing(tool == DrawTool.Erase);
    }

    // ── Page loading ──────────────────────────────────────────────────────────

    private async Task LoadPageAsync(int pageIdx)
    {
        _currentPageIdx = pageIdx;

        PrevBtn.IsEnabled   = false;
        NextBtn.IsEnabled   = false;
        ApplyBtn.IsEnabled  = false;
        PageLabel.Text      = $"Page {pageIdx + 1} / {_totalPages}";
        StatusLabel.Text    = "Loading…";
        PageContainer.Visibility = Visibility.Hidden;
        LoadingLabel.Visibility  = Visibility.Visible;

        // Keep existing strokes; only clear visual children that are on this page
        DrawCanvas.Children.Clear();
        _activePolyline = null;
        _activeStroke   = null;
        _isDrawing      = false;

        if (!_engine.IsOpen) { StatusLabel.Text = "No document open."; return; }

        var renderResult = await _engine.RenderPageAsync(pageIdx, RenderDpi);
        if (!renderResult.IsSuccess || renderResult.Value == null)
        {
            StatusLabel.Text = $"Render failed: {renderResult.Message}";
            return;
        }

        var bmp  = ToBitmapSource(renderResult.Value);
        double dipW = bmp.PixelWidth  * 96.0 / RenderDpi;
        double dipH = bmp.PixelHeight * 96.0 / RenderDpi;

        PageImage.Source = bmp;
        PageImage.Width  = dipW;
        PageImage.Height = dipH;
        DrawCanvas.Width  = dipW;
        DrawCanvas.Height = dipH;

        // Re-add existing strokes for this page
        if (_strokesByPage.TryGetValue(pageIdx, out var existing))
            foreach (var (poly, _) in existing)
                DrawCanvas.Children.Add(poly);

        PageContainer.Visibility = Visibility.Visible;
        LoadingLabel.Visibility  = Visibility.Collapsed;

        UpdateStatus();
        PrevBtn.IsEnabled  = pageIdx > 0;
        NextBtn.IsEnabled  = pageIdx < _totalPages - 1;
        ApplyBtn.IsEnabled = true;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private async void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPageIdx > 0) await LoadPageAsync(_currentPageIdx - 1);
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPageIdx < _totalPages - 1) await LoadPageAsync(_currentPageIdx + 1);
    }

    // ── Drawing events ────────────────────────────────────────────────────────

    private void DrawCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (_tool == DrawTool.Erase)
        {
            EraseAt(e.GetPosition(DrawCanvas));
            DrawCanvas.CaptureMouse();
            return;
        }

        _isDrawing = true;
        DrawCanvas.CaptureMouse();

        var pt = e.GetPosition(DrawCanvas);

        bool isHighlight = _tool == DrawTool.Highlight;
        var  color       = isHighlight ? _hlColor : _penColor;
        var  thickness   = isHighlight ? _hlThickness : _penThickness;
        byte opacity     = isHighlight ? (byte)(255 * _hlOpacity) : (byte)255;

        // Canvas visual
        _activePolyline = new Polyline
        {
            Stroke           = new SolidColorBrush(Color.FromArgb(opacity, color.R, color.G, color.B)),
            StrokeThickness  = thickness * Scale,
            StrokeLineJoin   = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        _activePolyline.Points.Add(pt);
        DrawCanvas.Children.Add(_activePolyline);

        // PDF-coords stroke record
        _activeStroke = new DrawingStroke
        {
            Thickness  = thickness,
            R          = color.R,
            G          = color.G,
            B          = color.B,
            A          = opacity,
            PageNumber = _currentPageIdx + 1,
        };
        _activeStroke.Points.Add((pt.X / Scale, pt.Y / Scale));

        e.Handled = true;
    }

    private void DrawCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(DrawCanvas);

        if (_tool == DrawTool.Erase)
        {
            MoveEraserRing(pos);
            if (e.LeftButton == MouseButtonState.Pressed)
                EraseAt(pos);
            return;
        }

        if (!_isDrawing || _activePolyline == null) return;

        _activePolyline.Points.Add(pos);
        _activeStroke!.Points.Add((pos.X / Scale, pos.Y / Scale));
    }

    private void DrawCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        DrawCanvas.ReleaseMouseCapture();

        if (_tool == DrawTool.Erase) return;

        if (!_isDrawing) return;
        _isDrawing = false;

        if (_activeStroke != null && _activePolyline != null)
        {
            if (!_strokesByPage.ContainsKey(_currentPageIdx))
                _strokesByPage[_currentPageIdx] = new();

            _strokesByPage[_currentPageIdx].Add((_activePolyline, _activeStroke));
            UpdateStatus();
        }

        _activePolyline = null;
        _activeStroke   = null;
    }

    private void DrawCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_eraserRing != null)
            _eraserRing.Visibility = Visibility.Collapsed;
    }

    // ── Eraser ────────────────────────────────────────────────────────────────

    private void ShowOrHideEraserRing(bool show)
    {
        if (!show)
        {
            if (_eraserRing != null) _eraserRing.Visibility = Visibility.Collapsed;
            return;
        }

        if (_eraserRing == null)
        {
            _eraserRing = new Ellipse
            {
                Width           = EraserRadius * 2,
                Height          = EraserRadius * 2,
                Stroke          = new SolidColorBrush(Color.FromRgb(200, 60, 60)),
                StrokeThickness = 1.5,
                Fill            = new SolidColorBrush(Color.FromArgb(30, 200, 60, 60)),
                IsHitTestVisible = false,
            };
            DrawCanvas.Children.Add(_eraserRing);
        }
        _eraserRing.Visibility = Visibility.Visible;
    }

    private void MoveEraserRing(Point pos)
    {
        if (_eraserRing == null) return;
        _eraserRing.Visibility = Visibility.Visible;
        Canvas.SetLeft(_eraserRing, pos.X - EraserRadius);
        Canvas.SetTop(_eraserRing,  pos.Y - EraserRadius);
    }

    private void EraseAt(Point cursorPos)
    {
        if (!_strokesByPage.TryGetValue(_currentPageIdx, out var list)) return;

        var toRemove = list
            .Where(entry => StrokeHitsPoint(entry.Visual, cursorPos, EraserRadius))
            .ToList();

        foreach (var entry in toRemove)
        {
            DrawCanvas.Children.Remove(entry.Visual);
            list.Remove(entry);
        }

        if (toRemove.Count > 0) UpdateStatus();
    }

    private static bool StrokeHitsPoint(Polyline poly, Point cursor, double radius)
    {
        var pts = poly.Points;
        if (pts.Count == 0) return false;
        if (pts.Count == 1)
            return Distance(pts[0], cursor) <= radius;

        for (var i = 1; i < pts.Count; i++)
        {
            if (DistanceToSegment(cursor, pts[i - 1], pts[i]) <= radius)
                return true;
        }
        return false;
    }

    private static double Distance(Point a, Point b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-10) return Distance(p, a);
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }

    // ── Undo / Clear ─────────────────────────────────────────────────────────

    private void Undo_Click(object sender, RoutedEventArgs e) => UndoLastStroke();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            UndoLastStroke();
            e.Handled = true;
        }
    }

    private void UndoLastStroke()
    {
        if (!_strokesByPage.TryGetValue(_currentPageIdx, out var list) || list.Count == 0) return;

        var last = list[^1];
        DrawCanvas.Children.Remove(last.Visual);
        list.RemoveAt(list.Count - 1);
        UpdateStatus();
    }

    private void ClearPage_Click(object sender, RoutedEventArgs e)
    {
        if (!_strokesByPage.TryGetValue(_currentPageIdx, out var list)) return;

        foreach (var (poly, _) in list)
            DrawCanvas.Children.Remove(poly);
        list.Clear();
        UpdateStatus();
    }

    // ── Result ────────────────────────────────────────────────────────────────

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var all = _strokesByPage.Values
            .SelectMany(list => list.Select(entry => entry.Data))
            .ToList();

        CollectedStrokes = all;
        DialogResult     = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpdateStatus()
    {
        int total = _strokesByPage.Values.Sum(l => l.Count);
        int onPage = _strokesByPage.TryGetValue(_currentPageIdx, out var l2) ? l2.Count : 0;

        StatusLabel.Text = total == 0
            ? "Draw on the page — use Pen, Highlight, or Erase tools"
            : $"{onPage} stroke(s) on this page  ·  {total} total across all pages";
    }

    private static BitmapSource ToBitmapSource(byte[] pngBytes)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(pngBytes);
        bmp.CacheOption  = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
