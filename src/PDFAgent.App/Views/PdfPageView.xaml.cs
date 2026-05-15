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
using PDFAgent.Core.Models;

namespace PDFAgent.App.Views;

public partial class PdfPageView : UserControl
{
    // ── Scale constant ────────────────────────────────────────────────────────
    //
    // Pages are rendered at 150 DPI with SetResolution(150,150) PNG metadata.
    // WPF's Image element with Stretch=None displays the image at:
    //   ActualWidth = pixelWidth × (96 / 150) = pageWidthPts × (150/72) × (96/150)
    //               = pageWidthPts × (96/72)
    //
    // So 1 PDF point = 96/72 WPF logical pixels (DIPs), always, regardless of
    // system DPI, layout timing, or WPF version. Using this constant eliminates
    // every runtime-measurement timing dependency.
    //
    private const double Scale = 96.0 / 72.0;   // DIPs per PDF point ≈ 1.333

    private static double Pts(double pdfPts) => pdfPts * Scale;

    // ── Overlay element dictionaries ──────────────────────────────────────────
    private readonly Dictionary<StickerViewModel, FrameworkElement>        _stickerElements     = new();
    private readonly Dictionary<TextAnnotationViewModel, FrameworkElement> _annotationElements  = new();
    private readonly Dictionary<TextEditWordViewModel, FrameworkElement>   _wordElements        = new();
    private readonly Dictionary<SearchHighlightRect, Rectangle>            _searchHighlights    = new();

    // ── Text selection state ──────────────────────────────────────────────────
    private record WordBox(PdfTextSegment Segment, Rect CanvasRect);
    private readonly List<WordBox> _wordBoxes = new();
    private bool  _isSelecting;
    private Point _selectionStart;

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
            old.Stickers.CollectionChanged         -= OnStickersChanged;
            old.TextAnnotations.CollectionChanged  -= OnTextAnnotationsChanged;
            old.EditableWords.CollectionChanged    -= OnEditableWordsChanged;
            old.SearchHighlights.CollectionChanged -= OnSearchHighlightsChanged;
            old.PropertyChanged                    -= OnPageItemPropertyChanged;
        }

        CancelSelection();
        _wordBoxes.Clear();

        StickerCanvas.Children.Clear();
        TextCanvas.Children.Clear();
        WordsCanvas.Children.Clear();
        SearchCanvas.Children.Clear();
        SelectionCanvas.Children.Clear();
        _stickerElements.Clear();
        _annotationElements.Clear();
        _wordElements.Clear();
        _searchHighlights.Clear();

        if (e.NewValue is RenderedPageItem item)
        {
            item.Stickers.CollectionChanged         += OnStickersChanged;
            item.TextAnnotations.CollectionChanged  += OnTextAnnotationsChanged;
            item.EditableWords.CollectionChanged    += OnEditableWordsChanged;
            item.SearchHighlights.CollectionChanged += OnSearchHighlightsChanged;
            item.PropertyChanged                    += OnPageItemPropertyChanged;

            // Build word boxes immediately — constant Scale means no layout dependency.
            RebuildWordBoxes();

            foreach (var s  in item.Stickers)         AddStickerElement(s);
            foreach (var ta in item.TextAnnotations)  AddTextAnnotationElement(ta);
            foreach (var h  in item.SearchHighlights) AddSearchHighlight(h);

            if (item.IsTextEditModeActive)
                foreach (var w in item.EditableWords) AddWordElement(w);
        }
    }

    private void OnPageItemPropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RenderedPageItem.TextLayer))
        {
            RebuildWordBoxes();
            return;
        }

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
                BlurRadius  = 8,
                ShadowDepth = 2,
                Opacity     = 0.4,
                Color       = Colors.Black,
            },
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Grid { Background = new SolidColorBrush(Color.FromArgb(200, 24, 36, 66)) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var deleteBtn = MakeOverlayButton("✕", Colors.White, Colors.IndianRed);
        deleteBtn.Command = vm.DeleteCommand;
        Grid.SetColumn(deleteBtn, 0);

        var label = new TextBlock
        {
            Text                = "✦ Drag to position",
            FontSize            = 10,
            Foreground          = new SolidColorBrush(Colors.White),
            VerticalAlignment   = VerticalAlignment.Center,
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

        var preview      = BuildSignaturePreview(vm.Bytes);
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

        MakeDraggable(outer, StickerCanvas, (x, y) => { vm.X = x; vm.Y = y; });
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

        var header = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 120, 215)),
            Cursor     = Cursors.SizeAll,
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });

        var handleLabel = new TextBlock
        {
            Text              = "  ✎ Text",
            FontSize          = 10,
            Foreground        = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(handleLabel, 0);

        var deleteBtn = MakeOverlayButton("×", Colors.White, Color.FromRgb(180, 40, 40));
        deleteBtn.FontSize = 14;
        deleteBtn.Command  = vm.DeleteCommand;
        Grid.SetColumn(deleteBtn, 1);

        header.Children.Add(handleLabel);
        header.Children.Add(deleteBtn);
        Grid.SetRow(header, 0);

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
        textBox.PreviewMouseLeftButtonDown += (_, e) => e.Handled = false;
        Grid.SetRow(textBox, 1);

        grid.Children.Add(header);
        grid.Children.Add(textBox);
        outer.Child = grid;

        Canvas.SetLeft(outer, vm.X);
        Canvas.SetTop(outer,  vm.Y);
        TextCanvas.Children.Add(outer);
        _annotationElements[vm] = outer;

        MakeDraggable(outer, TextCanvas,
            (x, y) => { vm.X = x; vm.Y = y; },
            skipSource: typeof(TextBox));

        vm.DeleteRequested += (s, _) =>
        {
            if (s is TextAnnotationViewModel tvm && PageItem != null)
                PageItem.TextAnnotations.Remove(tvm);
        };

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => textBox.Focus()));
    }

    private void RemoveTextAnnotationElement(TextAnnotationViewModel vm)
    {
        if (_annotationElements.Remove(vm, out var elem))
            TextCanvas.Children.Remove(elem);
    }

    // ── Editable-words (text-edit mode) ───────────────────────────────────────

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
            Text                     = vm.EditedText,
            FontSize                 = Math.Max(vm.CanvasHeight * 0.7, 9),
            Background               = Brushes.Transparent,
            BorderThickness          = new Thickness(0),
            Padding                  = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            AcceptsReturn            = false,
        };

        border.Child = tb;

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

    // ── Search highlights ─────────────────────────────────────────────────────

    private void OnSearchHighlightsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            SearchCanvas.Children.Clear();
            _searchHighlights.Clear();
            return;
        }
        if (e.NewItems != null)
            foreach (SearchHighlightRect h in e.NewItems) AddSearchHighlight(h);
        if (e.OldItems != null)
            foreach (SearchHighlightRect h in e.OldItems) RemoveSearchHighlight(h);
    }

    private void AddSearchHighlight(SearchHighlightRect h)
    {
        var rect = new Rectangle
        {
            Width            = Math.Max(Pts(h.Width),  4),
            Height           = Math.Max(Pts(h.Height), 6),
            Fill             = new SolidColorBrush(Color.FromArgb(110, 255, 200, 0)),
            Stroke           = new SolidColorBrush(Color.FromArgb(180, 200, 140, 0)),
            StrokeThickness  = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(rect, Pts(h.X));
        Canvas.SetTop(rect,  Pts(h.Y));
        SearchCanvas.Children.Add(rect);
        _searchHighlights[h] = rect;
    }

    private void RemoveSearchHighlight(SearchHighlightRect h)
    {
        if (_searchHighlights.Remove(h, out var rect))
            SearchCanvas.Children.Remove(rect);
    }

    // ── Word-box management (text selection) ──────────────────────────────────

    private void RebuildWordBoxes()
    {
        _wordBoxes.Clear();
        var layer = PageItem?.TextLayer;
        if (layer == null || layer.Count == 0)
        {
            PageGrid.Cursor = Cursors.Arrow;
            return;
        }

        foreach (var seg in layer)
        {
            _wordBoxes.Add(new WordBox(
                seg,
                new Rect(Pts(seg.X), Pts(seg.Y),
                         Math.Max(Pts(seg.Width),  4),
                         Math.Max(Pts(seg.Height), 6))));
        }

        PageGrid.Cursor = Cursors.IBeam;
    }

    // ── Mouse handlers ────────────────────────────────────────────────────────

    private void PageGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            CancelSelection();
            if (PageItem == null) return;
            var pos = e.GetPosition(PageGrid);
            var ann = new TextAnnotationViewModel { X = pos.X - 10, Y = pos.Y - 11 };
            PageItem.TextAnnotations.Add(ann);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 1)
        {
            CancelSelection();
            _isSelecting    = true;
            _selectionStart = e.GetPosition(PageGrid);
            PageGrid.CaptureMouse();
            e.Handled = true;
        }
    }

    private void PageGrid_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(PageGrid);
        if (_isSelecting)
            UpdateSelectionVisual(pos);
        else
            UpdateHoverHighlight(pos);
    }

    private void PageGrid_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting) return;
        _isSelecting = false;
        PageGrid.ReleaseMouseCapture();
        FinalizeSelection(e.GetPosition(PageGrid));
    }

    private void PageGrid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isSelecting)
            SelectionCanvas.Children.Clear();
    }

    // ── Selection visuals ─────────────────────────────────────────────────────

    private void UpdateHoverHighlight(Point pos)
    {
        if (_wordBoxes.Count == 0) return;
        SelectionCanvas.Children.Clear();
        var hovered = _wordBoxes.FirstOrDefault(wb => wb.CanvasRect.Contains(pos));
        if (hovered == null) return;

        var rect = new Rectangle
        {
            Width            = hovered.CanvasRect.Width,
            Height           = hovered.CanvasRect.Height,
            Fill             = new SolidColorBrush(Color.FromArgb(50, 0, 100, 220)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(rect, hovered.CanvasRect.X);
        Canvas.SetTop(rect,  hovered.CanvasRect.Y);
        SelectionCanvas.Children.Add(rect);
    }

    private void UpdateSelectionVisual(Point current)
    {
        SelectionCanvas.Children.Clear();
        var selRect = GetSelectionRect(current);

        foreach (var wb in _wordBoxes.Where(wb => wb.CanvasRect.IntersectsWith(selRect)))
        {
            var wordRect = new Rectangle
            {
                Width            = wb.CanvasRect.Width,
                Height           = wb.CanvasRect.Height,
                Fill             = new SolidColorBrush(Color.FromArgb(180, 0, 100, 220)),
                Stroke           = new SolidColorBrush(Color.FromArgb(255, 0,  80, 200)),
                StrokeThickness  = 1,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(wordRect, wb.CanvasRect.X);
            Canvas.SetTop(wordRect,  wb.CanvasRect.Y);
            SelectionCanvas.Children.Add(wordRect);
        }

        if (selRect.Width >= 1 || selRect.Height >= 1)
        {
            var band = new Rectangle
            {
                Width            = Math.Max(selRect.Width,  1),
                Height           = Math.Max(selRect.Height, 1),
                Fill             = new SolidColorBrush(Color.FromArgb(40, 0, 100, 220)),
                Stroke           = new SolidColorBrush(Color.FromArgb(220, 0, 100, 220)),
                StrokeThickness  = 2,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(band, selRect.X);
            Canvas.SetTop(band,  selRect.Y);
            SelectionCanvas.Children.Add(band);
        }
    }

    private void FinalizeSelection(Point endPos)
    {
        SelectionCanvas.Children.Clear();
        var selRect = GetSelectionRect(endPos);
        if (selRect.Width < 2 || selRect.Height < 2) return;

        if (_wordBoxes.Count == 0)
        {
            var msg = PageItem?.TextLayer == null
                ? "Text layer loading — try again in a moment"
                : "No text — this page is a scanned image (run OCR to add text)";
            ShowBriefMessage(msg);
            return;
        }

        var selected = _wordBoxes.Where(wb => wb.CanvasRect.IntersectsWith(selRect)).ToList();
        if (selected.Count == 0)
        {
            ShowBriefMessage("No text found in selected area");
            return;
        }

        foreach (var wb in selected)
        {
            var rect = new Rectangle
            {
                Width            = wb.CanvasRect.Width,
                Height           = wb.CanvasRect.Height,
                Fill             = new SolidColorBrush(Color.FromArgb(180, 0, 100, 220)),
                Stroke           = new SolidColorBrush(Color.FromArgb(255, 0,  80, 200)),
                StrokeThickness  = 1,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(rect, wb.CanvasRect.X);
            Canvas.SetTop(rect,  wb.CanvasRect.Y);
            SelectionCanvas.Children.Add(rect);
        }

        var text = string.Join(" ", selected.Select(wb => wb.Segment.Text));
        if (!string.IsNullOrEmpty(text))
            System.Windows.Clipboard.SetText(text);
    }

    private Rect GetSelectionRect(Point current) =>
        new(Math.Min(_selectionStart.X, current.X),
            Math.Min(_selectionStart.Y, current.Y),
            Math.Abs(current.X - _selectionStart.X),
            Math.Abs(current.Y - _selectionStart.Y));

    private void CancelSelection()
    {
        _isSelecting = false;
        if (PageGrid?.IsMouseCaptured == true) PageGrid.ReleaseMouseCapture();
        SelectionCanvas?.Children.Clear();
    }

    private void ShowBriefMessage(string message)
    {
        var border = new Border
        {
            Background       = new SolidColorBrush(Color.FromArgb(200, 40, 40, 40)),
            CornerRadius     = new CornerRadius(4),
            Padding          = new Thickness(10, 5, 10, 5),
            IsHitTestVisible = false,
        };
        border.Child = new TextBlock
        {
            Text       = message,
            Foreground = Brushes.White,
            FontSize   = 12,
        };
        Canvas.SetLeft(border, 20);
        Canvas.SetTop(border,  20);
        SelectionCanvas.Children.Add(border);

        var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SelectionCanvas.Children.Remove(border);
        };
        timer.Start();
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
        btn.MouseEnter += (_, _) => btn.Background = hoverColor;
        btn.MouseLeave += (_, _) => btn.Background = Brushes.Transparent;
        return btn;
    }

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
                        .Select(pt =>
                        {
                            var a = pt.EnumerateArray().ToArray();
                            return new Point(a[0].GetDouble(), a[1].GetDouble());
                        }))
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
                            .Select(pt =>
                            {
                                var a = pt.EnumerateArray().ToArray();
                                return new Point(a[0].GetDouble(), a[1].GetDouble());
                            })
                            .ToArray();
                        if (pts.Length < 2) continue;

                        var pl = new Polyline
                        {
                            Stroke             = Brushes.Black,
                            StrokeThickness    = 1.8,
                            StrokeStartLineCap = PenLineCap.Round,
                            StrokeEndLineCap   = PenLineCap.Round,
                            StrokeLineJoin     = PenLineJoin.Round,
                            Points             = new PointCollection(
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
                using var ms = new MemoryStream(bytes);
                var bmp      = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return new Image { Source = bmp, Stretch = Stretch.Uniform };
            }
            catch
            {
                return new TextBlock
                {
                    Text                = "Signature",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                };
            }
        }
    }
}
