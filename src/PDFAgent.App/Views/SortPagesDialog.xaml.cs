using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PDFAgent.App.Views;

public sealed class SortPageItem : INotifyPropertyChanged
{
    private int  _position;
    private bool _isDropTarget;

    public int    OriginalIndex { get; init; }
    public byte[]? Thumbnail    { get; init; }

    public int Position
    {
        get => _position;
        set { _position = value; Notify(); }
    }

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set { _isDropTarget = value; Notify(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public partial class SortPagesDialog : Window
{
    public ObservableCollection<SortPageItem> Items { get; } = new();

    /// <summary>0-based page indices in the new order chosen by the user.</summary>
    public IReadOnlyList<int> NewOrder => Items.Select(i => i.OriginalIndex).ToList();

    // ── Drag state ────────────────────────────────────────────────────────────
    private Point        _dragStart;
    private SortPageItem? _dragItem;

    public SortPagesDialog(IEnumerable<(int OriginalIndex, byte[]? Thumbnail)> pages)
    {
        InitializeComponent();
        PageList.ItemsSource = Items;

        foreach (var (origIdx, thumb) in pages)
            Items.Add(new SortPageItem { OriginalIndex = origIdx, Thumbnail = thumb });

        RenumberItems();
        UpdateSubtitle();
    }

    private void RenumberItems()
    {
        for (var i = 0; i < Items.Count; i++)
            Items[i].Position = i + 1;
    }

    private void UpdateSubtitle()
    {
        var count = Items.Count;
        SubtitleLabel.Text = $"{count} page{(count == 1 ? "" : "s")} — drag thumbnails to reorder or delete, or use Quick Sort";
    }

    private void ClearDropHighlights()
    {
        foreach (var item in Items)
            item.IsDropTarget = false;
    }

    // ── Quick sort ────────────────────────────────────────────────────────────

    private void Reverse_Click(object sender, RoutedEventArgs e)
    {
        var reversed = Items.Reverse().ToList();
        Items.Clear();
        foreach (var item in reversed) Items.Add(item);
        RenumberItems();
    }

    private void OddFirst_Click(object sender, RoutedEventArgs e)
    {
        // Pages with odd 1-based numbers (0,2,4,...) then even (1,3,5,...)
        var odds  = Items.Where(i => i.OriginalIndex % 2 == 0).ToList();
        var evens = Items.Where(i => i.OriginalIndex % 2 == 1).ToList();
        Items.Clear();
        foreach (var item in odds.Concat(evens)) Items.Add(item);
        RenumberItems();
    }

    private void EvenFirst_Click(object sender, RoutedEventArgs e)
    {
        var evens = Items.Where(i => i.OriginalIndex % 2 == 1).ToList();
        var odds  = Items.Where(i => i.OriginalIndex % 2 == 0).ToList();
        Items.Clear();
        foreach (var item in evens.Concat(odds)) Items.Add(item);
        RenumberItems();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var sorted = Items.OrderBy(i => i.OriginalIndex).ToList();
        Items.Clear();
        foreach (var item in sorted) Items.Add(item);
        RenumberItems();
    }

    // ── Drag-and-drop ─────────────────────────────────────────────────────────

    private void PageList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem  = null;

        var lbi = HitTestItem(e.GetPosition(PageList));
        if (lbi == null) return;

        _dragItem = lbi.DataContext as SortPageItem;
    }

    private void PageList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;

        var diff = _dragStart - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(PageList, _dragItem, DragDropEffects.Move);
        _dragItem = null;
    }

    private void PageList_DragOver(object sender, DragEventArgs e)
    {
        if (!IsInternalDrag(e)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }

        e.Effects = DragDropEffects.Move;

        var target = GetDropTarget(e);
        ClearDropHighlights();
        if (target >= 0 && target < Items.Count)
            Items[target].IsDropTarget = true;

        e.Handled = true;
    }

    private void PageList_DragLeave(object sender, DragEventArgs e)
        => ClearDropHighlights();

    private void PageList_Drop(object sender, DragEventArgs e)
    {
        ClearDropHighlights();

        if (!IsInternalDrag(e)) return;
        if (e.Data.GetData(typeof(SortPageItem)) is not SortPageItem dragged) return;

        var srcIdx    = Items.IndexOf(dragged);
        var targetIdx = GetDropTarget(e);

        if (srcIdx < 0 || targetIdx < 0 || srcIdx == targetIdx)
        { e.Handled = true; return; }

        // When moving forward, the effective insertion index shifts by one after removal.
        var dest = targetIdx > srcIdx ? targetIdx - 1 : targetIdx;
        dest = Math.Clamp(dest, 0, Items.Count - 1);

        Items.Move(srcIdx, dest);
        RenumberItems();
        e.Handled = true;
    }

    private int GetDropTarget(DragEventArgs e)
    {
        var pt  = e.GetPosition(PageList);
        var lbi = HitTestItem(pt);
        if (lbi == null) return Items.Count;

        var idx  = PageList.ItemContainerGenerator.IndexFromContainer(lbi);
        if (idx < 0) return Items.Count;

        // Left half of the card → insert before; right half → insert after
        var relX = e.GetPosition(lbi).X;
        return relX <= lbi.ActualWidth / 2 ? idx : idx + 1;
    }

    private static bool IsInternalDrag(DragEventArgs e)
        => e.Data.GetDataPresent(typeof(SortPageItem));

    private ListBoxItem? HitTestItem(Point relativeToList)
    {
        var hit = VisualTreeHelper.HitTest(PageList, relativeToList);
        if (hit == null) return null;

        var dep = hit.VisualHit as DependencyObject;
        while (dep != null)
        {
            if (dep is ListBoxItem lbi) return lbi;
            dep = VisualTreeHelper.GetParent(dep);
        }
        return null;
    }

    // ── Delete page ───────────────────────────────────────────────────────────

    private void DeletePage_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // prevent drag from starting on button click
        if ((sender as Button)?.DataContext is not SortPageItem item) return;
        Items.Remove(item);
        RenumberItems();
        UpdateSubtitle();
    }

    // ── Buttons ───────────────────────────────────────────────────────────────

    private void Apply_Click(object sender, RoutedEventArgs e)  => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
