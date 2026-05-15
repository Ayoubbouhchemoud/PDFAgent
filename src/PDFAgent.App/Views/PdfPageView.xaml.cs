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
    private readonly Dictionary<TextEditWordViewModel, FrameworkElement>   _wordElements       = new();

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
            old.EditableWords.CollectionChanged   -= OnEditableWordsChanged;
            old.PropertyChanged                   -= OnPageItemPropertyChanged;
        }

        StickerCanvas.Children.Clear();
        TextCanvas.Children.Clear();
        WordsCanvas.Children.Clear();
        _stickerElements.Clear();
        _annotationElements.Clear();
        _wordElements.Clear();

        if (e.NewValue is RenderedPageItem item)
        {
            item.Stickers.CollectionChanged        += OnStickersChanged;
            item.TextAnnotations.CollectionChanged += OnTextAnnotationsChanged;
            item.EditableWords.CollectionChanged   += OnEditableWordsChanged;
            item.PropertyChanged                   += OnPageItemPropertyChanged;

            foreach (var s  in item.Stickers)        AddStickerElement(s);
            foreach (var ta in item.TextAnnotations) AddTextAnnotationElement(ta);

            if (item.IsTextEditModeActive)
                foreach (var w in item.EditableWords) AddWordElement(w);
        }
    }

    private void OnPageItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RenderedPageItem.IsTextEditModeActive)) return;
        var item = PageItem;
        if (item == null) return;

        if (item.IsTextEditModeActive)
        {
            foreach (var w in item.EditableWords) AddWordElement(w);
        }
        else
        {
            WordsCanvas.Children.Clear();
            _wordElements.Clear();
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

    // ── EditableWords collection ──────────────────────────────────────────────

    private void OnEditableWordsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!(PageItem?.IsTextEditModeActive ?? false)) return;

        if (e.NewItems != null)
            foreach (TextEditWordViewModel vm in e.NewItems) AddWordElement(vm);

        if (e.OldItems != null)
            foreach (TextEditWordViewModel vm in e.OldItems) RemoveWordElement(vm);
    }

    private void AddWordElement(TextEditWordViewModel vm)
    {
        // Semi-transparent blue highlight rectangle
        var rect = new Border
        {
            Width           = Math.Max(vm.CanvasWidth, 4),
            Height          = Math.Max(vm.CanvasHeight, 8),
            Background      = new SolidColorBrush(Color.FromArgb(40, 0, 120, 215)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(120, 0, 100, 200)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(2),
            Cursor          = Cursors.IBeam,
            ToolTip         = vm.OriginalText,
        };

        Canvas.SetLeft(rect, vm.CanvasX);
        Canvas.SetTop(rect,  vm.CanvasY);
        WordsCanvas.Children.Add(rect);
        _wordElements[vm] = rect;

        rect.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            OpenWordEditor(vm, rect);
        };

        // Color overlay changes when word has been edited
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(TextEditWordViewModel.EditedText)) return;
            rect.Background = vm.IsEdited
                ? new SolidColorBrush(Color.FromArgb(70, 0, 180, 80))
                : new SolidColorBrush(Color.FromArgb(40, 0, 120, 215));
            rect.BorderBrush = vm.IsEdited
                ? new SolidColorBrush(Color.FromArgb(160, 0, 160, 60))
                : new SolidColorBrush(Color.FromArgb(120, 0, 100, 200));
        };
    }

    private void RemoveWordElement(TextEditWordViewModel vm)
    {
        if (_wordElements.Remove(vm, out var elem))
            WordsCanvas.Children.Remove(elem);
    }

    private void OpenWordEditor(TextEditWordViewModel vm, Border highlightRect)
    {
        // Remove existing editor if any
        if (WordsCanvas.Tag is FrameworkElement existing)
        {
            WordsCanvas.Children.Remove(existing);
            WordsCanvas.Tag = null;
        }

        var editorWidth  = Math.Max(vm.CanvasWidth  * 2.5, 120);
        var editorHeight = Math.Max(vm.CanvasHeight * 1.5, 28);

        var border = new Border
        {
            Width           = editorWidth,
            Height          = editorHeight,
            Background      = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
            BorderThickness = new Thickness(1.5),
            CornerRadius    = new CornerRadius(3),
            Effect          = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 2, Opacity = 0.3 },
            Padding         = new Thickness(3, 1, 3, 1),
        };

        var tb = new TextBox
        {
            Text            = vm.EditedText,
            FontSize        = Math.Max(vm.CanvasHeight * 0.7, 9),
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding         = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            AcceptsReturn   = false,
        };

        border.Child = tb;

        // Position the editor above or below the word
        var editorTop = vm.CanvasY - editorHeight - 2;
        if (editorTop < 0) editorTop = vm.CanvasY + vm.CanvasHeight + 2;

        Canvas.SetLeft(border, vm.CanvasX);
        Canvas.SetTop(border,  editorTop);
        WordsCanvas.Children.Add(border);
        WordsCanvas.Tag = border;

        tb.Focus();
        tb.SelectAll();

        tb.LostFocus += (_, _) => CommitWordEdit(vm, tb.Text, border);
        tb.KeyDown   += (_, e) =>
        {
            if (e.Key == Key.Return || e.Key == Key.Escape)
            {
                if (e.Key == Key.Escape) tb.Text = vm.OriginalText;
                CommitWordEdit(vm, tb.Text, border);
                e.Handled = true;
            }
        };
    }

    private void CommitWordEdit(TextEditWordViewModel vm, string newText, FrameworkElement editor)
    {
        vm.EditedText = newText.Trim();
        if (WordsCanvas.Tag == editor)
        {
            WordsCanvas.Children.Remove(editor);
            WordsCanvas.Tag = null;
        }
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
