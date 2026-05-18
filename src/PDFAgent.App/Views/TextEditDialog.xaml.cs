using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;

namespace PDFAgent.App.Views;

public partial class TextEditDialog : Window
{
    // Scale factor: PDF pts → WPF DIPs (constant regardless of render DPI)
    // Reason: pixelWidth = pts * (dpi/72); DIP width = pixels * (96/dpi) = pts * 96/72
    private const double Scale = 96.0 / 72.0;
    private const double RenderDpi = 150.0;

    private readonly IPdfEngine _engine;
    private readonly int _totalPages;
    private int _currentPageIdx;

    // Keyed by 0-based page index; populated lazily on first visit
    private readonly Dictionary<int, List<WordEditItem>> _editsByPage = new();

    // Active inline editor state
    private TextBox?  _activeTextBox;
    private Border?   _activeBorder;
    private WordEditItem? _activeWord;

    public IReadOnlyList<TextEditRecord> CollectedEdits { get; private set; } =
        Array.Empty<TextEditRecord>();

    public TextEditDialog(IPdfEngine engine, int totalPages)
    {
        _engine     = engine;
        _totalPages = totalPages;
        InitializeComponent();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadPageAsync(0);
    }

    // ── Page navigation ───────────────────────────────────────────────────────

    private async void Prev_Click(object sender, RoutedEventArgs e)
    {
        CommitActiveTextBox();
        if (_currentPageIdx > 0)
            await LoadPageAsync(_currentPageIdx - 1);
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        CommitActiveTextBox();
        if (_currentPageIdx < _totalPages - 1)
            await LoadPageAsync(_currentPageIdx + 1);
    }

    private async Task LoadPageAsync(int pageIdx)
    {
        _currentPageIdx = pageIdx;

        // Disable controls during load
        PrevBtn.IsEnabled = false;
        NextBtn.IsEnabled = false;
        ApplyBtn.IsEnabled = false;
        PageLabel.Text = $"Page {pageIdx + 1} / {_totalPages}";
        StatusLabel.Text = "Loading page…";
        PageContainer.Visibility = Visibility.Hidden;
        LoadingLabel.Visibility  = Visibility.Visible;
        OverlayCanvas.Children.Clear();

        if (!_engine.IsOpen) { StatusLabel.Text = "No document open."; return; }

        // Render the page at high quality
        var renderResult = await _engine.RenderPageAsync(pageIdx, RenderDpi);
        if (!renderResult.IsSuccess || renderResult.Value == null)
        {
            StatusLabel.Text = $"Render failed: {renderResult.Message}";
            return;
        }

        var bitmapSource = ToBitmapSource(renderResult.Value);
        // Natural display size (DIPs): width = pixelWidth * 96 / renderDpi
        double dipW = bitmapSource.PixelWidth  * 96.0 / RenderDpi;
        double dipH = bitmapSource.PixelHeight * 96.0 / RenderDpi;

        PageImage.Source = bitmapSource;
        PageImage.Width  = dipW;
        PageImage.Height = dipH;
        OverlayCanvas.Width  = dipW;
        OverlayCanvas.Height = dipH;

        // Extract words for this page if not already cached
        if (!_editsByPage.ContainsKey(pageIdx))
        {
            var textResult = await _engine.ExtractTextAsync(pageIdx);
            var segments   = (textResult.IsSuccess && textResult.Value != null)
                ? textResult.Value
                : Array.Empty<PdfTextSegment>();

            _editsByPage[pageIdx] = segments
                .Select(s => new WordEditItem(
                    PdfX: s.X, PdfY: s.Y, PdfWidth: s.Width, PdfHeight: s.Height,
                    PdfFontSize: s.FontSize, FontName: s.FontName,
                    IsBold: s.IsBold, IsItalic: s.IsItalic,
                    OriginalText: s.Text, PageNumber: pageIdx + 1))
                .ToList();
        }

        BuildWordOverlays();

        PageContainer.Visibility = Visibility.Visible;
        LoadingLabel.Visibility  = Visibility.Collapsed;

        var wordCount = _editsByPage[pageIdx].Count;
        StatusLabel.Text = wordCount > 0
            ? $"{wordCount} words on this page  ·  Click a word to edit it"
            : "No selectable text found on this page";

        PrevBtn.IsEnabled  = pageIdx > 0;
        NextBtn.IsEnabled  = pageIdx < _totalPages - 1;
        ApplyBtn.IsEnabled = true;
    }

    // ── Canvas overlay ────────────────────────────────────────────────────────

    private void BuildWordOverlays()
    {
        OverlayCanvas.Children.Clear();
        _activeTextBox = null;
        _activeBorder  = null;
        _activeWord    = null;

        var words = _editsByPage.GetValueOrDefault(_currentPageIdx) ?? [];

        foreach (var word in words)
        {
            double cx = word.PdfX      * Scale;
            double cy = word.PdfY      * Scale;
            double cw = Math.Max(word.PdfWidth  * Scale, 12);
            double ch = Math.Max(word.PdfHeight * Scale, 10);

            var border = new Border
            {
                Width           = cw,
                Height          = ch,
                Background      = Brushes.Transparent,
                BorderBrush     = new SolidColorBrush(Color.FromArgb(0, 0, 120, 215)),
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.IBeam,
                Tag             = word,
            };

            if (word.IsEdited)
                border.Background = new SolidColorBrush(Color.FromArgb(35, 255, 200, 0));

            Canvas.SetLeft(border, cx);
            Canvas.SetTop(border, cy);

            border.MouseEnter       += (_, _) => OnWordBorderHover(border, entering: true);
            border.MouseLeave       += (_, _) => OnWordBorderHover(border, entering: false);
            border.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                ActivateWordEditor((WordEditItem)border.Tag, border, cx, cy, cw, ch);
            };

            OverlayCanvas.Children.Add(border);
        }

        // Clicking the canvas background commits any active edit
        OverlayCanvas.MouseLeftButtonDown += (_, _) => CommitActiveTextBox();
    }

    private static void OnWordBorderHover(Border b, bool entering)
    {
        if (b.Visibility != Visibility.Visible) return;
        var word = (WordEditItem)b.Tag;
        b.BorderBrush = entering
            ? new SolidColorBrush(Color.FromArgb(160, 0, 120, 215))
            : new SolidColorBrush(Color.FromArgb(0, 0, 120, 215));
        if (!word.IsEdited)
            b.Background = entering
                ? new SolidColorBrush(Color.FromArgb(20, 0, 120, 215))
                : Brushes.Transparent;
    }

    private void ActivateWordEditor(WordEditItem word, Border border,
        double cx, double cy, double cw, double ch)
    {
        CommitActiveTextBox();

        _activeBorder = border;
        _activeWord   = word;
        border.Visibility = Visibility.Hidden;

        double tbW = Math.Max(cw, 50);
        double tbH = ch + 4;
        double fontSize = Math.Max(word.PdfFontSize * Scale * 0.78, 8);

        var tb = new TextBox
        {
            Width           = tbW,
            Height          = tbH,
            Text            = word.EditedText,
            FontSize        = fontSize,
            BorderThickness = new Thickness(1.5),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
            Background      = new SolidColorBrush(Color.FromArgb(240, 255, 255, 220)),
            Foreground      = Brushes.Black,
            Padding         = new Thickness(1, 0, 1, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = word,
        };

        Canvas.SetLeft(tb, cx);
        Canvas.SetTop(tb, cy - 2);

        tb.LostFocus += (_, _) => CommitActiveTextBox();
        tb.KeyDown   += (_, e) =>
        {
            if (e.Key is Key.Return or Key.Escape)
            {
                e.Handled = true;
                CommitActiveTextBox();
            }
        };

        _activeTextBox = tb;
        OverlayCanvas.Children.Add(tb);
        Dispatcher.InvokeAsync(() => { tb.Focus(); tb.SelectAll(); });
    }

    private void CommitActiveTextBox()
    {
        if (_activeTextBox == null) return;

        var word = (WordEditItem)_activeTextBox.Tag;
        word.EditedText = _activeTextBox.Text.Trim();

        OverlayCanvas.Children.Remove(_activeTextBox);
        _activeTextBox = null;

        if (_activeBorder != null)
        {
            _activeBorder.Visibility = Visibility.Visible;
            _activeBorder.Background = word.IsEdited
                ? new SolidColorBrush(Color.FromArgb(35, 255, 200, 0))
                : Brushes.Transparent;
            _activeBorder = null;
        }

        _activeWord = null;
        UpdateStatusEdit();
    }

    private void UpdateStatusEdit()
    {
        int total = _editsByPage.Values.Sum(l => l.Count(w => w.IsEdited));
        StatusLabel.Text = total == 0
            ? "No edits yet  ·  Click a word to edit it"
            : $"{total} edit(s) pending  ·  Click Apply Edits to save";
    }

    // ── Result collection ─────────────────────────────────────────────────────

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        CommitActiveTextBox();

        var records = _editsByPage
            .SelectMany(kv => kv.Value
                .Where(w => w.IsEdited)
                .Select(w => new TextEditRecord
                {
                    PageNumber = w.PageNumber,
                    X          = w.PdfX,
                    Y          = w.PdfY,
                    Width      = w.PdfWidth,
                    Height     = w.PdfHeight,
                    NewText    = w.EditedText,
                    FontSize   = w.PdfFontSize,
                    FontName   = w.FontName,
                    IsBold     = w.IsBold,
                    IsItalic   = w.IsItalic,
                }))
            .ToList();

        CollectedEdits = records;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ── Utilities ─────────────────────────────────────────────────────────────

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

    // ── Inner record ─────────────────────────────────────────────────────────

    private sealed class WordEditItem(
        double PdfX, double PdfY, double PdfWidth, double PdfHeight,
        double PdfFontSize, string? FontName, bool IsBold, bool IsItalic,
        string OriginalText, int PageNumber)
    {
        public double  PdfX        { get; } = PdfX;
        public double  PdfY        { get; } = PdfY;
        public double  PdfWidth    { get; } = PdfWidth;
        public double  PdfHeight   { get; } = PdfHeight;
        public double  PdfFontSize { get; } = PdfFontSize;
        public string? FontName    { get; } = FontName;
        public bool    IsBold      { get; } = IsBold;
        public bool    IsItalic    { get; } = IsItalic;
        public string  OriginalText{ get; } = OriginalText;
        public int     PageNumber  { get; } = PageNumber;
        public string  EditedText  { get; set; } = OriginalText;
        public bool    IsEdited    => EditedText != OriginalText
                                      && !string.IsNullOrWhiteSpace(EditedText);
    }
}
