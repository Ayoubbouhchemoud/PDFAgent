using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;

namespace PDFAgent.App.Views;

/// <summary>Observable item in the merge file list.</summary>
public sealed class MergeFileEntry : INotifyPropertyChanged
{
    private int _index;

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string SubText  => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(); }
    }

    public MergeFileEntry(string filePath, int index)
    {
        FilePath = filePath;
        _index   = index;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MergeDialog : Window
{
    private readonly ObservableCollection<MergeFileEntry> _entries = new();

    /// <summary>Ordered file paths chosen by the user. Valid after DialogResult = true.</summary>
    public IReadOnlyList<string> OrderedFiles =>
        _entries.Select(e => e.FilePath).ToList();

    public MergeDialog()
    {
        InitializeComponent();
        FileList.ItemsSource = _entries;
        UpdateSummary();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Pre-populates the list with the given file paths.</summary>
    public void PreloadFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths.Where(File.Exists))
            AddPath(p);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AddPath(string path)
    {
        if (_entries.Any(e => e.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;   // already in list
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
            0 => "No files added yet.  Click \"+ Add Files\" to begin.",
            1 => "1 file added — add at least one more to merge.",
            _ => $"{_entries.Count} files will be merged in the order shown above.",
        };
    }

    private void UpdateButtonStates()
    {
        var idx     = FileList.SelectedIndex;
        var hasItem = idx >= 0;
        RemoveBtn.IsEnabled   = hasItem;
        MoveUpBtn.IsEnabled   = hasItem && idx > 0;
        MoveDownBtn.IsEnabled = hasItem && idx < _entries.Count - 1;
    }

    private void ShowValidation(string msg)
    {
        ValidationMsg.Text       = msg;
        ValidationMsg.Visibility = Visibility.Visible;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter      = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Title       = "Add PDF files",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var path in dlg.FileNames)
            AddPath(path);

        ValidationMsg.Visibility = Visibility.Collapsed;
    }

    private void FileList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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
        // Keep selection on the same position (or last item)
        if (_entries.Count > 0)
            FileList.SelectedIndex = Math.Min(idx, _entries.Count - 1);
        UpdateButtonStates();
        ValidationMsg.Visibility = Visibility.Collapsed;
    }

    private void Merge_Click(object sender, RoutedEventArgs e)
    {
        ValidationMsg.Visibility = Visibility.Collapsed;

        if (_entries.Count < 2)
        {
            ShowValidation("Add at least 2 PDF files to merge.");
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
