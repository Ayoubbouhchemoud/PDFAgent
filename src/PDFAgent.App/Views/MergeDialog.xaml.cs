using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace PDFAgent.App.Views;

/// <summary>Observable item in the merge file list.</summary>
public sealed class MergeFileEntry : INotifyPropertyChanged
{
    private int    _index;
    private string _filePath;

    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(SubText));
        }
    }

    public string FileName => System.IO.Path.GetFileName(_filePath);
    public string SubText  => System.IO.Path.GetDirectoryName(_filePath) ?? string.Empty;

    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(); }
    }

    public MergeFileEntry(string filePath, int index)
    {
        _filePath = filePath;
        _index    = index;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MergeDialog : Window
{
    private readonly ObservableCollection<MergeFileEntry> _entries = new();

    // ── Drag state ────────────────────────────────────────────────────────────
    private Point _dragStartPoint;
    private int   _dragSourceIndex = -1;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Ordered file paths chosen by the user. Valid after DialogResult = true.</summary>
    public IReadOnlyList<string> OrderedFiles => _entries.Select(e => e.FilePath).ToList();

    public MergeDialog()
    {
        InitializeComponent();
        FileList.ItemsSource = _entries;
        UpdateSummary();
    }

    public void PreloadFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths.Where(IsSupportedFile))
            AddPath(p);
    }

    private static bool IsSupportedFile(string path)
    {
        if (!System.IO.File.Exists(path)) return false;
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".pdf" or ".doc" or ".docx";
    }

    // ── List management ───────────────────────────────────────────────────────

    private void AddPath(string path)
    {
        if (_entries.Any(e => e.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;
        _entries.Add(new MergeFileEntry(path, _entries.Count + 1));
        UpdateSummary();
    }

    private void RenumberEntries()
    {
        for (var i = 0; i < _entries.Count; i++)
            _entries[i].Index = i + 1;
    }

    private void UpdateSummary()
    {
        SummaryText.Text = _entries.Count switch
        {
            0 => "No files added yet.  Click \"+ Add Files\" or drop PDFs here.",
            1 => "1 file added — add at least one more to merge.",
            _ => $"{_entries.Count} files will be merged in the order shown above.",
        };
    }

    private void UpdateButtonStates()
    {
        var idx     = FileList.SelectedIndex;
        var hasItem = idx >= 0;
        RemoveBtn.IsEnabled   = hasItem;
        ReplaceBtn.IsEnabled  = hasItem;
        MoveUpBtn.IsEnabled   = hasItem && idx > 0;
        MoveDownBtn.IsEnabled = hasItem && idx < _entries.Count - 1;
    }

    private void ShowValidation(string msg)
    {
        ValidationMsg.Text       = msg;
        ValidationMsg.Visibility = Visibility.Visible;
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter      = "Supported documents (*.pdf;*.docx;*.doc)|*.pdf;*.docx;*.doc|PDF files (*.pdf)|*.pdf|Word documents (*.docx;*.doc)|*.docx;*.doc|All files (*.*)|*.*",
            Title       = "Add PDF or Word documents",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var path in dlg.FileNames) AddPath(path);
        ValidationMsg.Visibility = Visibility.Collapsed;
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        var idx = FileList.SelectedIndex;
        if (idx < 0) return;

        var dlg = new OpenFileDialog
        {
            Filter = "Supported documents (*.pdf;*.docx;*.doc)|*.pdf;*.docx;*.doc|PDF files (*.pdf)|*.pdf|Word documents (*.docx;*.doc)|*.docx;*.doc|All files (*.*)|*.*",
            Title  = "Replace with…",
        };
        if (dlg.ShowDialog() != true) return;

        var newPath = dlg.FileName;

        // Allow replacing with the same path (no-op) but reject duplicates at other positions.
        var duplicate = _entries
            .Where((entry, i) => i != idx)
            .Any(entry => entry.FilePath.Equals(newPath, StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            ShowValidation("That file is already in the list at a different position.");
            return;
        }

        _entries[idx] = new MergeFileEntry(newPath, idx + 1);
        FileList.SelectedIndex = idx;
        ValidationMsg.Visibility = Visibility.Collapsed;
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateButtonStates();

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var idx = FileList.SelectedIndex;
        if (idx <= 0) return;
        _entries.Move(idx, idx - 1);
        RenumberEntries();
        FileList.SelectedIndex = idx - 1;
        UpdateButtonStates();
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var idx = FileList.SelectedIndex;
        if (idx < 0 || idx >= _entries.Count - 1) return;
        _entries.Move(idx, idx + 1);
        RenumberEntries();
        FileList.SelectedIndex = idx + 1;
        UpdateButtonStates();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var idx = FileList.SelectedIndex;
        if (idx < 0) return;
        _entries.RemoveAt(idx);
        RenumberEntries();
        UpdateSummary();
        if (_entries.Count > 0)
            FileList.SelectedIndex = Math.Min(idx, _entries.Count - 1);
        UpdateButtonStates();
        ValidationMsg.Visibility = Visibility.Collapsed;
    }

    private void Merge_Click(object sender, RoutedEventArgs e)
    {
        ValidationMsg.Visibility = Visibility.Collapsed;
        if (_entries.Count < 2) { ShowValidation("Add at least 2 PDF files to merge."); return; }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ── Drag-and-drop: within the list (reorder) ──────────────────────────────

    private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint  = e.GetPosition(null);
        _dragSourceIndex = -1;

        // Only start drag tracking if the click landed on or inside a ListBoxItem.
        var item = HitTestListBoxItem(e.GetPosition(FileList));
        if (item != null)
            _dragSourceIndex = FileList.ItemContainerGenerator.IndexFromContainer(item);
    }

    private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSourceIndex < 0) return;

        var pos  = e.GetPosition(null);
        var diff = _dragStartPoint - pos;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // Confirm the source item is still valid before starting the drag loop.
        if (_dragSourceIndex >= _entries.Count) { _dragSourceIndex = -1; return; }

        var entry = _entries[_dragSourceIndex];
        DragDrop.DoDragDrop(FileList, entry, DragDropEffects.Move);
        _dragSourceIndex = -1;
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        if (IsInternalDrag(e) || IsFileDrop(e))
        {
            e.Effects = DragDropEffects.Move;
            var (_, indicatorY) = GetDropTarget(e);
            ShowDragIndicator(indicatorY);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void FileList_DragLeave(object sender, DragEventArgs e)
        => HideDragIndicator();

    private void FileList_Drop(object sender, DragEventArgs e)
    {
        HideDragIndicator();
        var (targetIdx, _) = GetDropTarget(e);

        if (IsInternalDrag(e) && e.Data.GetData(typeof(MergeFileEntry)) is MergeFileEntry dragged)
        {
            // Reorder within the list
            var srcIdx = _entries.IndexOf(dragged);
            if (srcIdx < 0 || srcIdx == targetIdx) return;

            var adjustedTarget = targetIdx > srcIdx ? targetIdx - 1 : targetIdx;
            _entries.Move(srcIdx, adjustedTarget);
            RenumberEntries();
            FileList.SelectedIndex = adjustedTarget;
            UpdateButtonStates();
        }
        else if (IsFileDrop(e) &&
                 e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            // Files dropped from Windows Explorer — insert at drop position
            var pdfs = files
                .Where(IsSupportedFile)
                .ToList();

            // Insert in reverse order so that the first dropped file ends up at targetIdx.
            for (var i = pdfs.Count - 1; i >= 0; i--)
            {
                var path = pdfs[i];
                if (_entries.Any(en => en.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    continue;   // skip duplicate
                var clamp = Math.Clamp(targetIdx, 0, _entries.Count);
                _entries.Insert(clamp, new MergeFileEntry(path, clamp + 1));
            }

            RenumberEntries();
            UpdateSummary();
            ValidationMsg.Visibility = Visibility.Collapsed;
        }

        e.Handled = true;
    }

    // ── Drag helpers ──────────────────────────────────────────────────────────

    private static bool IsInternalDrag(DragEventArgs e) =>
        e.Data.GetDataPresent(typeof(MergeFileEntry));

    private static bool IsFileDrop(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop);

    /// <summary>
    /// Determines the 0-based insertion index and the Y position (relative to DragCanvas)
    /// at which to draw the indicator line.
    /// </summary>
    private (int index, double indicatorY) GetDropTarget(DragEventArgs e)
    {
        var mouseY = e.GetPosition(DragCanvas).Y;

        for (var i = 0; i < _entries.Count; i++)
        {
            var container = FileList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
            if (container == null) continue;

            var itemTopLeft = container.TranslatePoint(new Point(0, 0), DragCanvas);
            var itemMidY    = itemTopLeft.Y + container.ActualHeight / 2;

            if (mouseY < itemMidY)
                return (i, itemTopLeft.Y);
        }

        // Below last item
        if (_entries.Count > 0)
        {
            var last = FileList.ItemContainerGenerator.ContainerFromIndex(_entries.Count - 1) as ListBoxItem;
            if (last != null)
            {
                var tl = last.TranslatePoint(new Point(0, 0), DragCanvas);
                return (_entries.Count, tl.Y + last.ActualHeight);
            }
        }

        return (0, 0);
    }

    private void ShowDragIndicator(double y)
    {
        DragCanvas.Children.Clear();
        DragCanvas.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width  = DragCanvas.ActualWidth,
            Height = 2,
            Fill   = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
        });
        Canvas.SetLeft(DragCanvas.Children[0], 0);
        Canvas.SetTop(DragCanvas.Children[0],  Math.Max(0, y - 1));
    }

    private void HideDragIndicator() => DragCanvas.Children.Clear();

    private ListBoxItem? HitTestListBoxItem(Point relativeToList)
    {
        var hit = VisualTreeHelper.HitTest(FileList, relativeToList);
        if (hit == null) return null;

        var dep = hit.VisualHit as DependencyObject;
        while (dep != null)
        {
            if (dep is ListBoxItem lbi) return lbi;
            dep = VisualTreeHelper.GetParent(dep);
        }
        return null;
    }
}
