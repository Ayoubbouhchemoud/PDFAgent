using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PDFAgent.App.ViewModels;

namespace PDFAgent.App.Views;

public partial class PdfPageView : UserControl
{
    // Tracks which WPF element belongs to which ViewModel so we can remove them.
    private readonly Dictionary<StickerViewModel, FrameworkElement>        _stickerElements    = new();
    private readonly Dictionary<TextAnnotationViewModel, FrameworkElement> _annotationElements = new();

    public PdfPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private RenderedPageItem? PageItem => DataContext as RenderedPageItem;

    // ── DataContext wiring ────────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is RenderedPageItem old)
        {
            old.Stickers.CollectionChanged        -= OnStickersChanged;
            old.TextAnnotations.CollectionChanged -= OnTextAnnotationsChanged;
        }

        StickerCanvas.Children.Clear();
        TextCanvas.Children.Clear();
        _stickerElements.Clear();
        _annotationElements.Clear();

        if (e.NewValue is RenderedPageItem item)
        {
            item.Stickers.CollectionChanged        += OnStickersChanged;
            item.TextAnnotations.CollectionChanged += OnTextAnnotationsChanged;

            foreach (var s  in item.Stickers)        AddStickerElement(s);
            foreach (var ta in item.TextAnnotations) AddTextAnnotationElement(ta);
        }
    }

    // ── Collection change handlers ────────────────────────────────────────────

    private void OnStickersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (StickerViewModel vm in e.NewItems) AddStickerElement(vm);

        if (e.OldItems != null)
            foreach (StickerViewModel vm in e.OldItems) RemoveStickerElement(vm);
    }

    private void OnTextAnnotationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (TextAnnotationViewModel vm in e.NewItems) AddTextAnnotationElement(vm);

        if (e.OldItems != null)
            foreach (TextAnnotationViewModel vm in e.OldItems) RemoveTextAnnotationElement(vm);
    }

    // ── Sticker element ───────────────────────────────────────────────────────

    private void AddStickerElement(StickerViewModel vm)
    {
        // Outer draggable border
        var outer = new Border
        {
            Width           = vm.Width,
            Height          = vm.Height + 30,
            BorderBrush     = new SolidColorBrush(Color.FromRgb(24, 36, 66)),
            BorderThickness = new Thickness(2),
            CornerRadius    = new CornerRadius(5),
            Background      = new SolidColorBrush(Color.FromArgb(18, 24, 36, 255)),
            Cursor          = Cursors.SizeAll,
            Effect          = new DropShadowEffect
            {
                BlurRadius   = 8,
                ShadowDepth  = 2,
                Opacity      = 0.4,
                Color        = Colors.Black,
            },
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Toolbar row ──
        var toolbar = new Grid { Background = new SolidColorBrush(Color.FromArgb(200, 24, 36, 66)) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var deleteBtn = MakeOverlayButton("✕", Colors.White, Colors.IndianRed);
        deleteBtn.Command = vm.DeleteCommand;
        Grid.SetColumn(deleteBtn, 0);

        var label = new TextBlock
        {
            Text              = "✦ Drag to position",
            FontSize          = 10,
            Foreground        = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetColumn(label, 1);

        var commitBtn = MakeOverlayButton("✓", Colors.White, Color.FromRgb(60, 160, 60));
        commitBtn.Command = vm.CommitCommand;
        Grid.SetColumn(commitBtn, 2);

        toolbar.Children.Add(deleteBtn);
        toolbar.Children.Add(label);
        toolbar.Children.Add(commitBtn);
        Grid.SetRow(toolbar, 0);

        // ── Signature preview ──
        var preview = BuildSignaturePreview(vm.Bytes);
        var previewBorder = new Border
        {
            Margin     = new Thickness(4, 2, 4, 4),
            Background = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            Child      = preview,
        };
        Grid.SetRow(previewBorder, 1);

        grid.Children.Add(toolbar);
        grid.Children.Add(previewBorder);
        outer.Child = grid;

        Canvas.SetLeft(outer, vm.X);
        Canvas.SetTop(outer,  vm.Y);
        StickerCanvas.Children.Add(outer);
        _stickerElements[vm] = outer;

        // Wire drag (skip if click is on a button)
        MakeDraggable(outer, StickerCanvas,
            (x, y) => { vm.X = x; vm.Y = y; });
    }

    private void RemoveStickerElement(StickerViewModel vm)
    {
        if (_stickerElements.Remove(vm, out var elem))
            StickerCanvas.Children.Remove(elem);
    }

    // ── Text annotation element ───────────────────────────────────────────────

    private void AddTextAnnotationElement(TextAnnotationViewModel vm)
    {
        var outer = new Border
        {
            MinWidth        = 80,
            Width           = vm.Width,
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
            BorderThickness = new Thickness(1.5),
            CornerRadius    = new CornerRadius(4),
            Background      = new SolidColorBrush(Color.FromArgb(230, 255, 252, 200)),
            Cursor          = Cursors.SizeAll,
            Effect          = new DropShadowEffect
            {
                BlurRadius  = 6,
                ShadowDepth = 1,
                Opacity     = 0.3,
                Color       = Colors.Black,
            },
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Header bar with drag handle and delete button
        var header = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 120, 215)),
            Cursor     = Cursors.SizeAll,
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });

        var handleLabel = new TextBlock
        {
            Text                = "  ✎ Text",
            FontSize            = 10,
            Foreground          = new SolidColorBrush(Colors.White),
            VerticalAlignment   = VerticalAlignment.Center,
        };
        Grid.SetColumn(handleLabel, 0);

        var deleteBtn = MakeOverlayButton("×", Colors.White, Color.FromRgb(180, 40, 40));
        deleteBtn.FontSize = 14;
        deleteBtn.Command  = vm.DeleteCommand;
        Grid.SetColumn(deleteBtn, 1);

        header.Children.Add(handleLabel);
        header.Children.Add(deleteBtn);
        Grid.SetRow(header, 0);

        // Editable text box
        var textBox = new TextBox
        {
            AcceptsReturn   = true,
            TextWrapping    = TextWrapping.Wrap,
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize        = vm.FontSize,
            Foreground      = new SolidColorBrush(Colors.Black),
            Padding         = new Thickness(4),
            MinHeight       = 30,
            Cursor          = Cursors.IBeam,
        };
        textBox.SetBinding(TextBox.TextProperty, new Binding(nameof(TextAnnotationViewModel.Text))
        {
            Source              = vm,
            Mode                = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        // TextBox handles its own mouse events — prevent drag from starting on it.
        textBox.PreviewMouseLeftButtonDown += (_, e) => e.Handled = false;
        Grid.SetRow(textBox, 1);

        grid.Children.Add(header);
        grid.Children.Add(textBox);
        outer.Child = grid;

        Canvas.SetLeft(outer, vm.X);
        Canvas.SetTop(outer,  vm.Y);
        TextCanvas.Children.Add(outer);
        _annotationElements[vm] = outer;

        // Drag via header or border (not textbox)
        MakeDraggable(outer, TextCanvas,
            (x, y) => { vm.X = x; vm.Y = y; },
            skipSource: typeof(TextBox));

        // Wire delete event
        vm.DeleteRequested += (s, _) =>
        {
            if (s is TextAnnotationViewModel tvm && PageItem != null)
            {
                PageItem.TextAnnotations.Remove(tvm);
            }
        };

        // Auto-focus so user can type immediately
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => textBox.Focus()));
    }

    private void RemoveTextAnnotationElement(TextAnnotationViewModel vm)
    {
        if (_annotationElements.Remove(vm, out var elem))
            TextCanvas.Children.Remove(elem);
    }

    // ── Double-click on page to add text annotation ───────────────────────────

    private void PageGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || PageItem == null) return;

        var pos = e.GetPosition(PageGrid);
        var ann = new TextAnnotationViewModel { X = pos.X - 10, Y = pos.Y - 11 };
        PageItem.TextAnnotations.Add(ann);
        e.Handled = true;
    }

    // ── Drag helper ───────────────────────────────────────────────────────────

    private static void MakeDraggable(
        FrameworkElement elem,
        Canvas parent,
        Action<double, double> onMove,
        Type? skipSource = null)
    {
        bool   dragging  = false;
        Point  dragStart = default;
        double origLeft  = 0;
        double origTop   = 0;

        elem.MouseLeftButtonDown += (s, e) =>
        {
            // Don't start drag when the user clicked a button or the skipped type
            if (e.OriginalSource is Button) return;
            if (skipSource != null && e.OriginalSource.GetType() == skipSource) return;

            dragging  = true;
            dragStart = e.GetPosition(parent);
            origLeft  = Canvas.GetLeft(elem);
            origTop   = Canvas.GetTop(elem);
            elem.CaptureMouse();
            e.Handled = true;
        };

        elem.MouseMove += (s, e) =>
        {
            if (!dragging) return;
            var pos     = e.GetPosition(parent);
            var newLeft = Math.Max(0, origLeft + (pos.X - dragStart.X));
            var newTop  = Math.Max(0, origTop  + (pos.Y - dragStart.Y));
            Canvas.SetLeft(elem, newLeft);
            Canvas.SetTop(elem,  newTop);
            onMove(newLeft, newTop);
            e.Handled = true;
        };

        elem.MouseLeftButtonUp += (s, e) =>
        {
            if (!dragging) return;
            dragging = false;
            elem.ReleaseMouseCapture();
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Button MakeOverlayButton(string content, Color fg, Color hoverBg)
    {
        var btn = new Button
        {
            Content         = content,
            Width           = 24,
            Height          = 24,
            Padding         = new Thickness(0),
            FontSize        = 12,
            FontWeight      = FontWeights.Bold,
            Foreground      = new SolidColorBrush(fg),
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor          = Cursors.Hand,
        };
        var hoverColor = new SolidColorBrush(Color.FromArgb(160, hoverBg.R, hoverBg.G, hoverBg.B));
        btn.MouseEnter  += (_, _) => btn.Background = hoverColor;
        btn.MouseLeave  += (_, _) => btn.Background = Brushes.Transparent;
        return btn;
    }

    // Reconstruct signature preview: vector JSON → Canvas of polylines, PNG → Image.
    private static UIElement BuildSignaturePreview(byte[] bytes)
    {
        if (bytes.Length > 0 && bytes[0] == (byte)'{')
        {
            var canvas = new Canvas { Background = Brushes.Transparent };
            try
            {
                using var doc  = JsonDocument.Parse(bytes);
                var root       = doc.RootElement;
                double canvasW = root.GetProperty("w").GetDouble();
                double canvasH = root.GetProperty("h").GetDouble();

                var allPts = root.GetProperty("s")
                    .EnumerateArray()
                    .SelectMany(stroke => stroke.EnumerateArray()
                        .Select(pt => { var a = pt.EnumerateArray().ToArray(); return new Point(a[0].GetDouble(), a[1].GetDouble()); }))
                    .ToList();

                if (allPts.Count > 0)
                {
                    double minX = allPts.Min(p => p.X), minY = allPts.Min(p => p.Y);
                    double maxX = allPts.Max(p => p.X), maxY = allPts.Max(p => p.Y);
                    double bboxW = Math.Max(maxX - minX, 1);
                    double bboxH = Math.Max(maxY - minY, 1);
                    const double previewW = 200, previewH = 70;
                    double scale = Math.Min(previewW / bboxW, previewH / bboxH) * 0.9;

                    canvas.Width  = previewW;
                    canvas.Height = previewH;

                    foreach (var stroke in root.GetProperty("s").EnumerateArray())
                    {
                        var pts = stroke.EnumerateArray()
                            .Select(pt => { var a = pt.EnumerateArray().ToArray(); return new Point(a[0].GetDouble(), a[1].GetDouble()); })
                            .ToArray();
                        if (pts.Length < 2) continue;

                        var pl = new Polyline
                        {
                            Stroke          = Brushes.Black,
                            StrokeThickness = 1.8,
                            StrokeStartLineCap = PenLineCap.Round,
                            StrokeEndLineCap   = PenLineCap.Round,
                            StrokeLineJoin     = PenLineJoin.Round,
                            Points          = new PointCollection(
                                pts.Select(p => new Point(
                                    (previewW - bboxW * scale) / 2 + (p.X - minX) * scale,
                                    (previewH - bboxH * scale) / 2 + (p.Y - minY) * scale))),
                        };
                        canvas.Children.Add(pl);
                    }
                }
                else
                {
                    canvas.Width  = 200;
                    canvas.Height = 70;
                }
            }
            catch
            {
                canvas.Width  = 200;
                canvas.Height = 70;
            }
            return canvas;
        }
        else
        {
            try
            {
                using var ms  = new MemoryStream(bytes);
                var bmp       = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return new Image { Source = bmp, Stretch = Stretch.Uniform };
            }
            catch
            {
                return new TextBlock { Text = "Signature", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
        }
    }
}
