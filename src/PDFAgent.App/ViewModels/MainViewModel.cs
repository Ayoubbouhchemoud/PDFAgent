using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PDFAgent.App.Services;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;

namespace PDFAgent.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly IPdfEngine _pdfEngine;
    private readonly IPdfEditor _pdfEditor;
    private readonly IPdfExporter _pdfExporter;
    private readonly IOcrEngine _ocrEngine;
    private readonly IRedactionEngine _redactionEngine;
    private readonly IFileDialogService _fileDialog;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private PdfDocumentInfo? _documentInfo;

    [ObservableProperty]
    private PdfPageInfo? _selectedPage;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages;

    [ObservableProperty]
    private double _currentZoom = 1.0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(SplitCommand))]
    [NotifyCanExecuteChangedFor(nameof(RotateCommand))]
    [NotifyCanExecuteChangedFor(nameof(OcrCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedactCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnnotateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyTextAnnotationsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportToImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    [NotifyCanExecuteChangedFor(nameof(PropertiesCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleTextEditModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyTextEditsCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReduceSizeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertToPdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertFromPdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(SortPagesCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isMergeMode;

    public ObservableCollection<MergeQueueItem> MergeQueueItems { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SplitCommand))]
    private bool _isSplitMode;

    public ObservableCollection<SplitPreviewPageItem> SplitPreviewItems { get; } = new();
    public int SelectedSplitCount => SplitPreviewItems.Count(i => i.IsSelected);

    public event Action<int>? ZoomPageRequested;

    [ObservableProperty]
    private bool _isTextEditMode;

    private readonly Stack<string> _undoStack = new();

    public ObservableCollection<ThumbnailItem> Thumbnails { get; } = new();
    public ObservableCollection<RenderedPageItem> RenderedPages { get; } = new();
    public List<double> ZoomLevels { get; } = new() { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0 };

    public MainViewModel(
        ILogger<MainViewModel> logger,
        IPdfEngine pdfEngine,
        IPdfEditor pdfEditor,
        IPdfExporter pdfExporter,
        IOcrEngine ocrEngine,
        IRedactionEngine redactionEngine,
        IFileDialogService fileDialog)
    {
        _logger = logger;
        _pdfEngine = pdfEngine;
        _pdfEditor = pdfEditor;
        _pdfExporter = pdfExporter;
        _ocrEngine = ocrEngine;
        _redactionEngine = redactionEngine;
        _fileDialog = fileDialog;
    }

    public async Task InitializeAsync()
    {
        if (AppArguments.TryGetFilePath(out var filePath) && filePath != null)
            await OpenFileAsync(filePath);
    }

    private bool DocumentReady() => IsDocumentLoaded && !IsBusy;

    // ── File ────────────────────────────────────────────────────────────────

    // Cancelled and replaced every time a new document is opened or the engine is closed.
    private CancellationTokenSource _renderCts = new();

    // Separate CTS for text extraction — only cancelled on close/new-open, NOT on zoom/rotate.
    private CancellationTokenSource _textExtractCts = new();

    [RelayCommand]
    private async Task OpenFileAsync(string? filePath = null)
    {
        // Guard: don't interrupt an in-progress operation (e.g. rotate or redact).
        // IsBusy is true during any async command; a second open would race the engine.
        if (IsBusy) return;

        filePath ??= _fileDialog.OpenPdf();
        if (string.IsNullOrEmpty(filePath)) return;

        // Cancel any background render that is still in flight for the previous document.
        _renderCts.Cancel();
        _renderCts = new CancellationTokenSource();
        var ct = _renderCts.Token;

        IsBusy = true;
        StatusText = $"Opening {Path.GetFileName(filePath)}…";
        try
        {
            if (_pdfEngine.IsOpen)
                await _pdfEngine.CloseAsync();

            var result = await _pdfEngine.OpenAsync(filePath, ct: ct);
            if (!result.IsSuccess || result.Value == null)
            {
                StatusText = $"Failed: {result.Message}";
                return;
            }

            DocumentInfo = result.Value;
            TotalPages = result.Value.PageCount;
            CurrentPage = 1;
            ClearUndoStack();
            OpenedFilePath = filePath;
            StatusText = $"Opened {result.Value.FileName} ({result.Value.PageCount} pages)";

            await LoadThumbnailsAsync(ct);
            await RenderCurrentPagesAsync(ct);
        }
        catch (OperationCanceledException) { /* new document opened, silently abort */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task SaveFileAsync()
    {
        var path = _fileDialog.SavePdf(DocumentInfo?.FileName ?? "output.pdf");
        if (path == null) return;

        IsBusy = true;
        try
        {
            await Task.Run(() => File.Copy(_pdfEngine.FilePath, path, overwrite: true));
            StatusText = $"Saved to {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task CloseFileAsync()
    {
        _renderCts.Cancel();
        _renderCts = new CancellationTokenSource();
        _textExtractCts.Cancel();
        _textExtractCts = new CancellationTokenSource();

        if (_pdfEngine.IsOpen)
            await _pdfEngine.CloseAsync();

        DocumentInfo   = null;
        OpenedFilePath = null;
        TotalPages     = 0;
        CurrentPage    = 1;

        RenderedPages.Clear();
        Thumbnails.Clear();
        ClearUndoStack();
        IsTextEditMode = false;
        IsMergeMode    = false;
        MergeQueueItems.Clear();
        IsSplitMode = false;
        SplitPreviewItems.Clear();
        IsSearchVisible = false;
        SearchQuery = null;
        SearchStatus = string.Empty;
        SearchHitCount = 0;
        ClearSearchState();
        StatusText = "Ready";
    }

    // ── Edit ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Merge()
    {
        var initial = _pdfEngine.IsOpen ? new[] { _pdfEngine.FilePath } : Array.Empty<string>();

        var orderedFiles = _fileDialog.ShowMergeDialog(initial);
        if (orderedFiles == null || orderedFiles.Count < 2) return;

        MergeQueueItems.Clear();
        for (var i = 0; i < orderedFiles.Count; i++)
            MergeQueueItems.Add(new MergeQueueItem(orderedFiles[i], i + 1));

        IsMergeMode = true;
        StatusText  = $"{orderedFiles.Count} files queued — loading previews…";

        _ = LoadMergeQueueThumbnailsAsync();
    }

    private async Task LoadMergeQueueThumbnailsAsync()
    {
        foreach (var item in MergeQueueItems.ToList())
        {
            if (!IsMergeMode) return;

            if (!item.IsWordDocument)
            {
                try
                {
                    var (thumb, count) = await _pdfEngine.RenderExternalPreviewAsync(item.FilePath);
                    item.ThumbnailBytes = thumb;
                    item.PageCount      = count;
                }
                catch { /* leave thumbnail null, placeholder shows */ }
            }

            item.IsLoading = false;
        }

        if (IsMergeMode)
            StatusText = $"{MergeQueueItems.Count} files queued — review the order, then click Execute Merge.";
    }

    [RelayCommand]
    private void CancelMergeMode()
    {
        IsMergeMode = false;
        MergeQueueItems.Clear();
        StatusText = IsDocumentLoaded
            ? $"{DocumentInfo?.FileName} — {TotalPages} page(s)"
            : "Ready";
    }

    [RelayCommand]
    private async Task ExecuteMergeAsync()
    {
        if (MergeQueueItems.Count < 2)
        {
            StatusText = "Add at least 2 files to merge.";
            return;
        }

        var files    = MergeQueueItems.Select(i => i.FilePath).ToList();
        var baseName = Path.GetFileNameWithoutExtension(files[0]);
        var output   = _fileDialog.SavePdf($"{baseName}_merged.pdf");
        if (output == null) return;   // user cancelled the save dialog

        IsMergeMode = false;
        MergeQueueItems.Clear();

        // PdfiumViewer holds a read lock on the open file.
        // If that file is also in the merge list we must close it first.
        var currentPath = _pdfEngine.IsOpen ? _pdfEngine.FilePath : null;
        var currentInQueue = currentPath != null &&
            files.Any(f => f.Equals(currentPath, StringComparison.OrdinalIgnoreCase));

        IsBusy = true;
        StatusText = $"Merging {files.Count} files…";
        try
        {
            if (currentInQueue)
                await _pdfEngine.CloseAsync();

            var result = await _pdfEditor.MergeAsync(files, output);
            if (result.IsSuccess)
            {
                StatusText = $"Merge complete — {result.Message}";
                await OpenFileAsync(output);
            }
            else
            {
                StatusText = $"Merge failed: {result.Message}";
                System.Windows.MessageBox.Show(
                    result.Message, "Merge Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                if (currentInQueue && currentPath != null)
                    await OpenFileAsync(currentPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteMerge failed");
            StatusText = $"Merge error: {ex.Message}";
            System.Windows.MessageBox.Show(
                ex.Message, "Merge Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void MoveMergeItemUp(MergeQueueItem item)
    {
        var idx = MergeQueueItems.IndexOf(item);
        if (idx <= 0) return;
        MergeQueueItems.Move(idx, idx - 1);
        RenumberMergeQueue();
    }

    [RelayCommand]
    private void MoveMergeItemDown(MergeQueueItem item)
    {
        var idx = MergeQueueItems.IndexOf(item);
        if (idx < 0 || idx >= MergeQueueItems.Count - 1) return;
        MergeQueueItems.Move(idx, idx + 1);
        RenumberMergeQueue();
    }

    [RelayCommand]
    private void RemoveMergeItem(MergeQueueItem item)
    {
        MergeQueueItems.Remove(item);
        RenumberMergeQueue();
        if (MergeQueueItems.Count == 0)
            StatusText = "No files in merge queue.";
    }

    private void RenumberMergeQueue()
    {
        for (var i = 0; i < MergeQueueItems.Count; i++)
            MergeQueueItems[i].Index = i + 1;
    }

    private bool CanSplit() => IsDocumentLoaded && !IsBusy && !IsSplitMode;

    [RelayCommand(CanExecute = nameof(CanSplit))]
    private void Split()
    {
        SplitPreviewItems.Clear();
        foreach (var thumb in Thumbnails)
        {
            var item = new SplitPreviewPageItem
            {
                PageNumber     = thumb.PageNumber,
                ThumbnailBytes = thumb.Thumbnail,
            };
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SplitPreviewPageItem.IsSelected))
                    OnPropertyChanged(nameof(SelectedSplitCount));
            };
            SplitPreviewItems.Add(item);
        }
        IsSplitMode = true;
        StatusText  = $"{TotalPages} page(s) — check pages to extract, or click Split All.";
    }

    [RelayCommand]
    private void CancelSplitMode()
    {
        IsSplitMode = false;
        SplitPreviewItems.Clear();
        StatusText = IsDocumentLoaded ? $"{DocumentInfo?.FileName} — {TotalPages} page(s)" : "Ready";
    }

    [RelayCommand]
    private void SelectAllSplitPages()
    {
        foreach (var item in SplitPreviewItems)
            item.IsSelected = true;
        OnPropertyChanged(nameof(SelectedSplitCount));
    }

    [RelayCommand]
    private void DeselectAllSplitPages()
    {
        foreach (var item in SplitPreviewItems)
            item.IsSelected = false;
        OnPropertyChanged(nameof(SelectedSplitCount));
    }

    [RelayCommand]
    private void ZoomSplitPage(SplitPreviewPageItem item) =>
        ZoomPageRequested?.Invoke(item.PageNumber);

    [RelayCommand]
    private async Task ExecuteSplitSelectedAsync()
    {
        var selected = SplitPreviewItems.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Select at least one page to extract.";
            return;
        }

        var baseName   = Path.GetFileNameWithoutExtension(DocumentInfo!.FileName);
        var outputFile = _fileDialog.SavePdf($"{baseName}_extracted.pdf");
        if (outputFile == null) return;

        IsSplitMode = false;
        SplitPreviewItems.Clear();

        var opts = new SplitOptions
        {
            Mode        = SplitMode.SplitRange,
            OutputFile  = outputFile,
            PageIndices = selected.Select(i => i.PageNumber - 1).ToList(),
            BaseName    = baseName,
        };

        IsBusy     = true;
        StatusText = $"Extracting {selected.Count} page(s)…";
        try
        {
            var result = await _pdfEditor.SplitAsync(_pdfEngine.FilePath, opts);
            StatusText = result.IsSuccess
                ? $"Extracted {selected.Count} page(s) → {Path.GetFileName(outputFile)}"
                : $"Extract failed: {result.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteSplitSelected failed");
            StatusText = $"Extract error: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ExecuteSplitAllAsync()
    {
        var outputDir = _fileDialog.SelectFolder();
        if (outputDir == null) return;

        var baseName = Path.GetFileNameWithoutExtension(DocumentInfo!.FileName);
        IsSplitMode = false;
        SplitPreviewItems.Clear();

        var opts = new SplitOptions
        {
            Mode      = SplitMode.SplitAll,
            OutputDir = outputDir,
            BaseName  = baseName,
        };

        IsBusy     = true;
        StatusText = $"Splitting {TotalPages} page(s)…";
        try
        {
            var result = await _pdfEditor.SplitAsync(_pdfEngine.FilePath, opts);
            StatusText = result.IsSuccess
                ? $"Split complete — {result.Message}"
                : $"Split failed: {result.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteSplitAll failed");
            StatusText = $"Split error: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    public async Task<byte[]?> RenderPageForZoomAsync(int pageIndex)
    {
        if (!_pdfEngine.IsOpen) return null;
        var result = await _pdfEngine.RenderPageAsync(pageIndex, dpi: 200);
        return result.IsSuccess ? result.Value : null;
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task RotateAsync()
    {
        // Ask the user which pages and what angle before doing anything.
        var opts = _fileDialog.ShowRotateDialog(CurrentPage, TotalPages);
        if (opts == null) return; // user cancelled

        var pages = opts.PageSelection switch
        {
            Services.RotatePageSelection.All         => Enumerable.Range(0, TotalPages).ToList(),
            Services.RotatePageSelection.CurrentPage => new List<int> { CurrentPage - 1 },
            Services.RotatePageSelection.Range       =>
                Services.PageRangeParser.Parse(opts.PageRangeText, TotalPages).ToList(),
            _ => Enumerable.Range(0, TotalPages).ToList(),
        };

        if (pages.Count == 0)
        {
            StatusText = "Rotate: no valid pages in range — operation cancelled.";
            return;
        }

        IsBusy = true;
        StatusText = $"Rotating {pages.Count} page(s) by {opts.Degrees}°…";
        var path = _pdfEngine.FilePath;
        string? failureStatus = null;
        string? undoSnap = null;
        try
        {
            // PdfiumViewer holds an exclusive lock on the file.
            // Close it first so PdfSharp can overwrite it, then reopen.
            undoSnap = MakeUndoSnapshot();
            await _pdfEngine.CloseAsync();

            var result = await _pdfEditor.RotatePagesAsync(path, pages, opts.Degrees);
            if (result.IsSuccess)
            {
                _undoStack.Push(undoSnap);
                undoSnap = null;
                StatusText = $"Rotation complete — {result.Message}";
            }
            else
                failureStatus = StatusText = $"Rotation failed: {result.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rotate failed");
            failureStatus = StatusText = $"Rotate error: {ex.Message}";
        }
        finally
        {
            // Always reopen so the viewer reflects the (possibly rotated) file.
            // Create a fresh CTS so any previous stale render token doesn't interfere.
            _renderCts.Cancel();
            _renderCts = new CancellationTokenSource();
            var reopenCt = _renderCts.Token;
            try
            {
                var reopen = await _pdfEngine.OpenAsync(path, ct: reopenCt);
                if (reopen.IsSuccess && reopen.Value != null)
                {
                    DocumentInfo = reopen.Value;
                    TotalPages = reopen.Value.PageCount;
                    await LoadThumbnailsAsync(reopenCt);
                    // Preserve failure status so the user sees the error after rendering finishes.
                    await RenderCurrentPagesAsync(reopenCt, finalStatus: failureStatus);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reopen file after rotate");
            }
            try { if (undoSnap != null && File.Exists(undoSnap)) File.Delete(undoSnap); } catch { }
            UndoCommand.NotifyCanExecuteChanged();
            IsBusy = false;
        }
    }

    // ── Sort Pages ──────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task SortPagesAsync()
    {
        // Snapshot thumbnails before showing the dialog (while app is fully interactive).
        var snapshot = Thumbnails
            .Select(t => (t.PageNumber - 1, t.Thumbnail))
            .ToList();

        var dlg = new Views.SortPagesDialog(snapshot)
            { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        var newOrder = dlg.NewOrder;

        // Skip if order is unchanged
        if (newOrder.Count == TotalPages &&
            newOrder.Select((origIdx, pos) => origIdx == pos).All(v => v))
        {
            StatusText = "Pages are already in this order — no changes made.";
            return;
        }

        var path      = _pdfEngine.FilePath;
        string? undoSnap  = null;
        string? failStatus = null;

        IsBusy = true;
        StatusText = "Sorting pages…";
        try
        {
            undoSnap = MakeUndoSnapshot();
            await _pdfEngine.CloseAsync();

            var result = await _pdfEditor.ReorderPagesAsync(path, newOrder);
            if (result.IsSuccess)
            {
                _undoStack.Push(undoSnap);
                undoSnap = null;
                StatusText = $"Pages sorted — {result.Message}";
            }
            else
            {
                failStatus = StatusText = $"Sort failed: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SortPages failed");
            failStatus = StatusText = $"Sort error: {ex.Message}";
        }
        finally
        {
            _renderCts.Cancel();
            _renderCts = new CancellationTokenSource();
            var ct = _renderCts.Token;
            try
            {
                var reopen = await _pdfEngine.OpenAsync(path, ct: ct);
                if (reopen.IsSuccess && reopen.Value != null)
                {
                    DocumentInfo = reopen.Value;
                    TotalPages   = reopen.Value.PageCount;
                    CurrentPage  = 1;
                    await LoadThumbnailsAsync(ct);
                    ViewerRefreshRequested?.Invoke();
                    await RenderCurrentPagesAsync(ct, finalStatus: failStatus);
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to reopen after sort"); }
            try { if (undoSnap != null && File.Exists(undoSnap)) File.Delete(undoSnap); } catch { }
            UndoCommand.NotifyCanExecuteChanged();
            IsBusy = false;
        }
    }

    // ── Tools ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task OcrAsync()
    {
        if (!_ocrEngine.IsAvailable)
        {
            var tessdataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PDFAgent", "tessdata");
            System.Windows.MessageBox.Show(
                $"OCR requires Tesseract language data files.\n\n" +
                $"1. Download 'eng.traineddata' from:\n" +
                $"   https://github.com/tesseract-ocr/tessdata\n\n" +
                $"2. Place it in:\n   {tessdataPath}\n\n" +
                $"3. Restart the application.",
                "OCR Unavailable",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            StatusText = "OCR unavailable — tessdata missing (see popup for instructions)";
            return;
        }

        IsBusy = true;
        StatusText = "Running OCR…";
        try
        {
            var sb = new StringBuilder();
            for (var i = 0; i < TotalPages; i++)
            {
                StatusText = $"OCR: processing page {i + 1} of {TotalPages}…";
                var renderResult = await _pdfEngine.RenderPageAsync(i, dpi: 300);
                if (!renderResult.IsSuccess || renderResult.Value == null) continue;

                var ocrResult = await _ocrEngine.ProcessPageAsync(renderResult.Value);
                if (ocrResult.IsSuccess && ocrResult.Value != null)
                {
                    sb.AppendLine($"=== Page {i + 1} ===");
                    sb.AppendLine(ocrResult.Value.FullText);
                    sb.AppendLine();
                    _logger.LogInformation("OCR p{Page}: {Chars} chars, {Conf:P0} confidence",
                        i + 1, ocrResult.Value.FullText.Length, ocrResult.Value.Confidence / 100.0);
                }
            }

            var outputPath = _fileDialog.SaveTextFile(
                Path.GetFileNameWithoutExtension(DocumentInfo!.FileName) + "_ocr.txt");
            if (outputPath != null)
            {
                await File.WriteAllTextAsync(outputPath, sb.ToString());
                StatusText = $"OCR complete — saved to {Path.GetFileName(outputPath)}";
            }
            else
            {
                StatusText = "OCR complete (not saved)";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR failed");
            StatusText = $"OCR error: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task RedactAsync()
    {
        var output = _fileDialog.SavePdf(
            Path.GetFileNameWithoutExtension(DocumentInfo!.FileName) + "_redacted.pdf");
        if (output == null) return;

        IsBusy = true;
        StatusText = "Redacting PII…";
        try
        {
            var result = await _redactionEngine.RedactPiiAsync(
                _pdfEngine.FilePath, output, new PiiRedactionProfile
                {
                    RedactEmails = true,
                    RedactPhoneNumbers = true,
                    RedactSsn = true,
                    RedactCreditCards = true,
                });
            StatusText = result.IsSuccess
                ? $"Redaction complete — {result.Message}"
                : $"Redaction failed: {result.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task ReduceSizeAsync()
    {
        var dlg = new Views.ReduceSizeDialog(DocumentInfo!.FileSizeBytes)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        if (dlg.ShowDialog() != true) return;

        var imageDpi  = dlg.SelectedImageDpi;
        var quality   = dlg.SelectedJpegQuality;
        var baseName  = Path.GetFileNameWithoutExtension(DocumentInfo.FileName);
        var suffix    = imageDpi == null ? "_compressed" : $"_{imageDpi}dpi_q{quality}";
        var output    = _fileDialog.SavePdf($"{baseName}{suffix}.pdf");
        if (output == null) return;

        IsBusy = true;
        var origSize = DocumentInfo.FileSizeBytes;
        StatusText = "Compressing PDF…";
        try
        {
            var progress = new Progress<double>(p =>
                StatusText = $"Compressing… {p:P0}");

            var result = await _pdfEditor.CompressAsync(
                _pdfEngine.FilePath, output, imageDpi, quality, progress);

            if (result.IsSuccess)
            {
                var newSize  = new FileInfo(output).Length;
                var savedPct = origSize > 0 ? (1.0 - (double)newSize / origSize) * 100 : 0;
                StatusText = $"Compressed: {FormatFileSize(origSize)} → {FormatFileSize(newSize)} " +
                             $"(−{savedPct:N1}%) → {Path.GetFileName(output)}";
                _logger.LogInformation("ReduceSize complete: {Result}", result.Message);
            }
            else
            {
                StatusText = $"Compression failed: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReduceSize failed");
            StatusText = $"Compression error: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:N1} MB";
        if (bytes >= 1_024)     return $"{bytes / 1_024.0:N0} KB";
        return $"{bytes} B";
    }

    // ── Convert to PDF ───────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertToPdfAsync()
    {
        var files = _fileDialog.OpenForConversion();
        if (files.Count == 0) return;

        IsBusy = true;
        try
        {
            // Multiple images → combine into one PDF
            var imageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif" };

            if (files.Count > 1 && files.All(f => imageExts.Contains(Path.GetExtension(f))))
            {
                var baseName = Path.GetFileNameWithoutExtension(files[0]);
                var output   = _fileDialog.SavePdf($"{baseName}_combined.pdf");
                if (output == null) return;

                StatusText = $"Combining {files.Count} images…";
                var progress = new Progress<double>(p => StatusText = $"Converting… {p:P0}");
                var result   = await _pdfEditor.ConvertImagesToPdfAsync(files, output, progress);
                StatusText   = result.IsSuccess
                    ? $"Done — {result.Message}"
                    : $"Failed: {result.Message}";
                if (result.IsSuccess) await TryOpenResultAsync(output);
                return;
            }

            // Single file or mixed types — convert each individually
            foreach (var file in files)
            {
                var baseName = Path.GetFileNameWithoutExtension(file);
                var output   = _fileDialog.SavePdf($"{baseName}.pdf");
                if (output == null) continue;

                StatusText = $"Converting {Path.GetFileName(file)}…";
                var result = await _pdfEditor.ConvertToPdfAsync(file, output);
                StatusText = result.IsSuccess
                    ? $"Done — {result.Message}"
                    : $"Failed: {result.Message}";
                if (result.IsSuccess && files.Count == 1)
                    await TryOpenResultAsync(output);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConvertToPdf failed");
            StatusText = $"Conversion error: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private bool CanConvert() => !IsBusy;

    // ── Convert PDF to HTML ───────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task ConvertFromPdfAsync()
    {
        var dlg = new Views.ConvertFromPdfDialog(
            DocumentInfo!.FileName, TotalPages, CurrentPage)
        { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        var format   = dlg.SelectedFormat;
        var baseName = Path.GetFileNameWithoutExtension(DocumentInfo.FileName);

        // Collect the save path before entering IsBusy — dialogs need the window interactive.
        bool multiPage = TotalPages > 1;
        var outputPath = format switch
        {
            ExportFormat.Docx => _fileDialog.SaveDocxFile($"{baseName}.docx"),
            ExportFormat.Xlsx => _fileDialog.SaveXlsxFile($"{baseName}.xlsx"),
            ExportFormat.Md   => _fileDialog.SaveMdFile($"{baseName}.md"),
            ExportFormat.Png  => multiPage ? _fileDialog.SaveZipFile($"{baseName}_pages.zip")
                                           : _fileDialog.SavePngFile($"{baseName}.png"),
            ExportFormat.Jpg  => multiPage ? _fileDialog.SaveZipFile($"{baseName}_pages.zip")
                                           : _fileDialog.SaveJpgFile($"{baseName}.jpg"),
            ExportFormat.Svg  => multiPage ? _fileDialog.SaveZipFile($"{baseName}_pages.zip")
                                           : _fileDialog.SaveSvgFile($"{baseName}.svg"),
            _                 => _fileDialog.SaveHtmlFile($"{baseName}.html"),
        };
        if (outputPath == null) return;

        IsBusy = true;
        try
        {
            string label = format switch
            {
                ExportFormat.Docx => "Word document",
                ExportFormat.Xlsx => "Excel workbook",
                ExportFormat.Md   => "Markdown",
                ExportFormat.Png  => multiPage ? "PNG images (ZIP)" : "PNG image",
                ExportFormat.Jpg  => multiPage ? "JPEG images (ZIP)" : "JPEG image",
                ExportFormat.Svg  => multiPage ? "SVG images (ZIP)" : "SVG image",
                _                 => "HTML",
            };
            StatusText = $"Exporting as {label}…";
            var result = await _pdfExporter.ExportAsync(
                _pdfEngine.FilePath, outputPath, format);

            if (result.IsSuccess)
            {
                StatusText = $"Exported → {Path.GetFileName(outputPath)}";
                ShowConvertSuccess(result.Message ?? "Export complete.", outputPath);
            }
            else
            {
                StatusText = "Export failed";
                ShowConvertError(result.Message ?? "Export failed.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConvertFromPdf failed");
            StatusText = $"Export error: {ex.Message}";
            ShowConvertError($"Export failed:\n\n{ex.Message}");
        }
        finally { IsBusy = false; }
    }

    private static void ShowConvertSuccess(string message, string path, bool isFolder = false)
    {
        var choice = System.Windows.MessageBox.Show(
            message + "\n\nOpen it now?",
            "Conversion Complete",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Information);

        if (choice == System.Windows.MessageBoxResult.Yes)
        {
            try
            {
                if (isFolder)
                    System.Diagnostics.Process.Start("explorer.exe", path);
                else
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName        = path,
                        UseShellExecute = true,
                    });
            }
            catch { }
        }
    }

    private static void ShowConvertError(string message)
    {
        System.Windows.MessageBox.Show(
            message,
            "Conversion Failed",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }

    private async Task TryOpenResultAsync(string pdfPath)
    {
        var open = System.Windows.MessageBox.Show(
            $"Conversion complete.\n\nOpen '{Path.GetFileName(pdfPath)}' in PDF Agent?",
            "Convert to PDF", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (open == System.Windows.MessageBoxResult.Yes)
            await OpenFileAsync(pdfPath);
    }

    public event EventHandler?    SignRequested;
    // Fired after any in-place PDF modification is saved and the engine re-opened.
    // MainWindow handles this by reloading WebView2 so the viewer shows the updated file.
    public event Action?          ViewerRefreshRequested;

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private void Sign() => SignRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Places a signature image on the specified page at the chosen preset position,
    /// then overwrites the file and triggers a WebView2 reload.
    /// Called directly from the placement dialog — replaces the old sticker overlay path.
    /// </summary>
    public async Task PlaceSignatureAsync(
        byte[] signatureBytes,
        int pageNumber,
        SignaturePlacement placement,
        double signatureWidthPts  = 200,
        double signatureHeightPts = 80)
    {
        var opts = new SignatureOverlayOptions
        {
            ImageBytes      = signatureBytes,
            PageNumber      = pageNumber,
            Placement       = placement,
            SignatureWidth  = signatureWidthPts,
            SignatureHeight = signatureHeightPts,
            Margin          = 36,
        };
        await ApplySignatureAsync(opts);
    }

    public async Task<byte[]?> RenderPagePreviewAsync(int pageIndex)
    {
        if (!_pdfEngine.IsOpen) return null;
        var result = await _pdfEngine.RenderPageAsync(pageIndex, dpi: 96);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task ApplySignatureAsync(SignatureOverlayOptions opts)
    {
        if (!_pdfEngine.IsOpen) return;
        var path = _pdfEngine.FilePath;
        var tmp  = Path.GetTempFileName();
        string? undoSnap = null;
        IsBusy = true;
        StatusText = "Applying signature…";
        string? failureStatus = null;

        try
        {
            undoSnap = MakeUndoSnapshot();
            // Release the file lock so PdfSharp can open the same file.
            await _pdfEngine.CloseAsync();

            var result = await _pdfEditor.AddSignatureImageAsync(path, tmp, opts);
            if (result.IsSuccess)
            {
                File.Move(tmp, path, overwrite: true);
                tmp = null;
                _undoStack.Push(undoSnap);
                undoSnap = null;
                _logger.LogInformation("Signature applied → {Path}", path);
            }
            else
            {
                failureStatus = $"Signing failed: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplySignature failed");
            failureStatus = $"Sign error: {ex.Message}";
        }
        finally
        {
            try { if (undoSnap != null && File.Exists(undoSnap)) File.Delete(undoSnap); } catch { }
            if (tmp != null && File.Exists(tmp)) File.Delete(tmp);

            // Always reopen — same pattern as RotateAsync.
            _renderCts.Cancel();
            _renderCts = new CancellationTokenSource();
            var ct = _renderCts.Token;
            try
            {
                var reopen = await _pdfEngine.OpenAsync(path, ct: ct);
                if (reopen.IsSuccess && reopen.Value != null)
                {
                    DocumentInfo = reopen.Value;
                    TotalPages   = reopen.Value.PageCount;
                    await LoadThumbnailsAsync(ct);
                    await RenderCurrentPagesAsync(ct, finalStatus: failureStatus);
                    if (failureStatus == null)
                        ViewerRefreshRequested?.Invoke();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reopen after sign");
                StatusText = "Error reopening document after sign";
            }

            if (failureStatus == null) StatusText = "Signature applied";
            UndoCommand.NotifyCanExecuteChanged();
            IsBusy = false;
        }
    }

    // ── Sticker / Text-annotation management ────────────────────────────────

    public StickerViewModel AddStickerToCurrentPage(byte[] signatureBytes)
    {
        if (CurrentPage < 1 || CurrentPage > RenderedPages.Count)
            throw new InvalidOperationException("No current page");

        var pageItem = RenderedPages[CurrentPage - 1];
        var sticker  = new StickerViewModel
        {
            Bytes      = signatureBytes,
            ParentPage = pageItem,
            X          = 120,
            Y          = 120,
            Width      = 220,
            Height     = 90,
        };
        pageItem.Stickers.Add(sticker);
        return sticker;
    }

    public void RemoveSticker(StickerViewModel sticker) =>
        sticker.ParentPage?.Stickers.Remove(sticker);

    public async Task CommitStickerAsync(StickerViewModel sticker)
    {
        if (sticker.ParentPage == null || !_pdfEngine.IsOpen) return;

        // PdfiumViewer embeds the render DPI in the PNG, so WPF displays the image at
        // pixelWidth * (96 / renderDpi) DIPs.  Canvas coordinates are therefore WPF DIPs
        // (96 DPI reference), and the conversion to PDF points is 72/96 = 0.75.
        // The sticker outer Border starts at vm.Y, but its first row is the 30-DIP toolbar;
        // the signature image itself lives in the second row, so Y must be offset by 30 DIPs.
        const double ratio       = 72.0 / 96.0;   // WPF DIPs → PDF points
        const double toolbarDips = 30.0;           // toolbar row height in DIPs

        var opts = new SignatureOverlayOptions
        {
            ImageBytes      = sticker.Bytes,
            PageNumber      = sticker.ParentPage.PageNumber,
            AbsoluteX       = sticker.X * ratio,
            AbsoluteY       = (sticker.Y + toolbarDips) * ratio,
            SignatureWidth  = sticker.Width  * ratio,
            SignatureHeight = sticker.Height * ratio,
        };

        sticker.ParentPage.Stickers.Remove(sticker);
        await ApplySignatureAsync(opts);
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task ApplyTextAnnotationsAsync() => await BakeTextAnnotationsAsync();

    public async Task BakeTextAnnotationsAsync()
    {
        var records = RenderedPages
            .SelectMany(p => p.TextAnnotations
                .Where(a => !string.IsNullOrWhiteSpace(a.Text))
                .Select(a => new TextAnnotationRecord
                {
                    PageNumber = p.PageNumber,
                    X          = a.X              * (72.0 / 96.0),
                    Y          = (a.Y + 22.0)     * (72.0 / 96.0),   // +22 for header bar
                    Width      = a.Width           * (72.0 / 96.0),
                    Height     = a.Height          * (72.0 / 96.0),
                    Text       = a.Text,
                    FontSize   = a.FontSize,
                }))
            .ToList();

        if (records.Count == 0)
        {
            StatusText = "No text annotations to apply";
            return;
        }

        var path = _pdfEngine.FilePath;
        var tmp  = Path.GetTempFileName();
        string? undoSnap = null;
        IsBusy = true;
        StatusText = "Applying text edits…";
        string? failureStatus = null;
        try
        {
            undoSnap = MakeUndoSnapshot();
            await _pdfEngine.CloseAsync();
            var result = await _pdfEditor.BakeTextAnnotationsAsync(path, tmp, records);
            if (result.IsSuccess)
            {
                File.Move(tmp, path, overwrite: true);
                tmp = null;
                _undoStack.Push(undoSnap);
                undoSnap = null;
            }
            else
            {
                failureStatus = $"Text apply failed: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BakeTextAnnotations failed");
            failureStatus = $"Text error: {ex.Message}";
        }
        finally
        {
            try { if (undoSnap != null && File.Exists(undoSnap)) File.Delete(undoSnap); } catch { }
            if (tmp != null && File.Exists(tmp)) File.Delete(tmp);
            _renderCts.Cancel();
            _renderCts = new CancellationTokenSource();
            var ct = _renderCts.Token;
            try
            {
                var reopen = await _pdfEngine.OpenAsync(path, ct: ct);
                if (reopen.IsSuccess && reopen.Value != null)
                {
                    DocumentInfo = reopen.Value;
                    TotalPages   = reopen.Value.PageCount;
                    foreach (var p in RenderedPages) p.TextAnnotations.Clear();
                    await LoadThumbnailsAsync(ct);
                    await RenderCurrentPagesAsync(ct, finalStatus: failureStatus);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reopen after BakeText failed");
            }
            if (failureStatus == null) StatusText = "Text edits applied";
            UndoCommand.NotifyCanExecuteChanged();
            IsBusy = false;
        }
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    private bool CanUndo() => IsDocumentLoaded && !IsBusy && _undoStack.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (!_undoStack.TryPop(out var snap) || !_pdfEngine.IsOpen) return;

        var path = _pdfEngine.FilePath;
        IsBusy = true;
        StatusText = "Undoing…";
        try
        {
            await _pdfEngine.CloseAsync();
            File.Copy(snap, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Undo copy failed");
            _undoStack.Push(snap);   // restore so the entry isn't lost
            StatusText = $"Undo failed: {ex.Message}";
            snap = null;             // don't delete the file we failed to restore from
        }
        finally
        {
            try { if (snap != null && File.Exists(snap)) File.Delete(snap); } catch { }
        }

        _renderCts.Cancel();
        _renderCts = new CancellationTokenSource();
        var ct = _renderCts.Token;
        try
        {
            var reopen = await _pdfEngine.OpenAsync(path, ct: ct);
            if (reopen.IsSuccess && reopen.Value != null)
            {
                DocumentInfo = reopen.Value;
                TotalPages   = reopen.Value.PageCount;
                await LoadThumbnailsAsync(ct);
                await RenderCurrentPagesAsync(ct);
                StatusText = "Undo complete";
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Undo reopen failed"); }

        UndoCommand.NotifyCanExecuteChanged();
        IsBusy = false;
    }

    private string MakeUndoSnapshot()
    {
        var snap = Path.Combine(Path.GetTempPath(), $"pdfagent_undo_{Guid.NewGuid():N}.pdf");
        File.Copy(_pdfEngine.FilePath, snap);
        return snap;
    }

    private void ClearUndoStack()
    {
        while (_undoStack.TryPop(out var snap))
            try { if (File.Exists(snap)) File.Delete(snap); } catch { }
        UndoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task AnnotateAsync()
    {
        var output = _fileDialog.SavePdf(
            Path.GetFileNameWithoutExtension(DocumentInfo!.FileName) + "_annotated.pdf");
        if (output == null) return;

        IsBusy = true;
        StatusText = "Adding annotation…";
        try
        {
            var annotationText = $"Reviewed — {Environment.UserName} — {DateTime.Now:yyyy-MM-dd}";
            var result = await _pdfEditor.AddPageAnnotationAsync(
                _pdfEngine.FilePath, output, CurrentPage, annotationText);
            StatusText = result.IsSuccess
                ? $"Annotation added to page {CurrentPage} — saved to {Path.GetFileName(output)}"
                : $"Annotation failed: {result.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Annotate failed");
            StatusText = $"Annotation error: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task ExportToImageAsync()
    {
        var baseName = Path.GetFileNameWithoutExtension(DocumentInfo!.FileName);
        var output = _fileDialog.SaveImageFile($"{baseName}_page{CurrentPage}.png");
        if (output == null) return;

        IsBusy = true;
        StatusText = $"Exporting page {CurrentPage} as image…";
        try
        {
            var result = await _pdfEngine.RenderPageAsync(CurrentPage - 1, dpi: 150);
            if (!result.IsSuccess || result.Value == null)
            {
                StatusText = $"Export failed: {result.Message}";
                return;
            }

            await File.WriteAllBytesAsync(output, result.Value);
            StatusText = $"Exported page {CurrentPage} → {Path.GetFileName(output)}";
            _logger.LogInformation("Exported page {Page} as image → {Output}", CurrentPage, output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExportToImage failed");
            StatusText = $"Export error: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private void Print()
    {
        _fileDialog.PrintFile(_pdfEngine.FilePath);
        StatusText = $"Print job sent for {DocumentInfo!.FileName}";
    }

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private void Properties()
    {
        _fileDialog.ShowProperties(DocumentInfo!);
    }

    // ── Add Page ────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task AddPageAsync()
    {
        // Fetch current page's dimensions so the dialog can show them.
        var pageResult = await _pdfEngine.GetPageAsync(CurrentPage - 1);
        var w = pageResult.IsSuccess && pageResult.Value != null ? pageResult.Value.WidthPoints  : 595.28;
        var h = pageResult.IsSuccess && pageResult.Value != null ? pageResult.Value.HeightPoints : 841.89;

        var dlgResult = _fileDialog.ShowAddPageDialog(CurrentPage, TotalPages, w, h);
        if (dlgResult == null) return;

        // Translate position choice to a 0-based insert index.
        var insertAt = dlgResult.Position switch
        {
            PDFAgent.App.Services.AddPagePosition.BeforeCurrent => CurrentPage - 1,
            PDFAgent.App.Services.AddPagePosition.AfterCurrent  => CurrentPage,
            PDFAgent.App.Services.AddPagePosition.AtBeginning   => 0,
            PDFAgent.App.Services.AddPagePosition.AtEnd         => TotalPages,
            _                                                   => CurrentPage,
        };

        var path = _pdfEngine.FilePath;
        var tmp  = Path.GetTempFileName();
        string? undoSnap = null;
        IsBusy = true;
        StatusText = "Adding blank page…";
        string? failStatus = null;

        try
        {
            undoSnap = MakeUndoSnapshot();
            await _pdfEngine.CloseAsync();

            var result = await _pdfEditor.AddBlankPageAsync(
                path, tmp, insertAt, dlgResult.WidthPts, dlgResult.HeightPts);

            if (result.IsSuccess)
            {
                File.Move(tmp, path, overwrite: true);
                tmp = null;
                _undoStack.Push(undoSnap);
                undoSnap = null;
                UndoCommand.NotifyCanExecuteChanged();
            }
            else
            {
                failStatus = $"Add page failed: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddPage failed");
            failStatus = $"Add page error: {ex.Message}";
        }
        finally
        {
            try { if (undoSnap != null && File.Exists(undoSnap)) File.Delete(undoSnap); } catch { }
            if (tmp != null && File.Exists(tmp)) File.Delete(tmp);

            _renderCts.Cancel();
            _renderCts = new CancellationTokenSource();
            var ct = _renderCts.Token;
            try
            {
                var reopen = await _pdfEngine.OpenAsync(path, ct: ct);
                if (reopen.IsSuccess && reopen.Value != null)
                {
                    DocumentInfo = reopen.Value;
                    TotalPages   = reopen.Value.PageCount;

                    // Navigate to the new page so user sees it.
                    if (failStatus == null)
                        CurrentPage = Math.Clamp(insertAt + 1, 1, reopen.Value.PageCount);

                    await LoadThumbnailsAsync(ct);
                    await RenderCurrentPagesAsync(ct, finalStatus: failStatus);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogError(ex, "Reopen after AddPage failed"); }

            IsBusy = false;
        }
    }

    // ── Text Editing ────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(DocumentReady))]
    private async Task ToggleTextEditModeAsync()
    {
        IsTextEditMode = !IsTextEditMode;

        if (IsTextEditMode)
        {
            StatusText = "Loading text…";
            IsBusy = true;
            try
            {
                // Extract words for every rendered page in parallel
                const double ratio = 96.0 / 72.0;   // PDF pts → canvas DIPs
                var tasks = RenderedPages.Select(async pageItem =>
                {
                    var result = await _pdfEngine.ExtractTextAsync(pageItem.PageNumber - 1);
                    return (pageItem, result);
                }).ToList();

                var results = await Task.WhenAll(tasks);

                foreach (var (pageItem, result) in results)
                {
                    pageItem.EditableWords.Clear();
                    if (!result.IsSuccess || result.Value == null) continue;

                    int idx = 0;
                    foreach (var seg in result.Value)
                    {
                        pageItem.EditableWords.Add(new TextEditWordViewModel
                        {
                            CanvasX      = seg.X * ratio,
                            CanvasY      = seg.Y * ratio,
                            CanvasWidth  = seg.Width  * ratio,
                            CanvasHeight = seg.Height * ratio,
                            PdfX         = seg.X,
                            PdfY         = seg.Y,
                            PdfWidth     = seg.Width,
                            PdfHeight    = seg.Height,
                            OriginalText = seg.Text,
                            EditedText   = seg.Text,
                            FontSize     = seg.FontSize,
                        });
                        idx++;
                    }

                    pageItem.IsTextEditModeActive = true;
                }

                var totalWords = RenderedPages.Sum(p => p.EditableWords.Count);
                StatusText = totalWords > 0
                    ? $"Text edit mode — {totalWords} words detected. Click a word to edit."
                    : "Text edit mode — no selectable text found on these pages.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load text for editing");
                StatusText = $"Text load error: {ex.Message}";
                IsTextEditMode = false;
            }
            finally { IsBusy = false; }
        }
        else
        {
            // Exit text edit mode — clear word overlays
            foreach (var pageItem in RenderedPages)
            {
                pageItem.IsTextEditModeActive = false;
                pageItem.EditableWords.Clear();
            }
            ApplyTextEditsCommand.NotifyCanExecuteChanged();
            StatusText = $"{DocumentInfo?.FileName} — {TotalPages} page(s)";
        }
    }

    private bool CanApplyTextEdits() =>
        IsDocumentLoaded && !IsBusy && IsTextEditMode &&
        RenderedPages.Any(p => p.EditableWords.Any(w => w.IsEdited));

    [RelayCommand(CanExecute = nameof(CanApplyTextEdits))]
    private async Task ApplyTextEditsAsync()
    {
        var edits = RenderedPages
            .SelectMany(p => p.EditableWords
                .Where(w => w.IsEdited)
                .Select(w => new PDFAgent.Core.Models.TextEditRecord
                {
                    PageNumber = p.PageNumber,
                    X          = w.PdfX,
                    Y          = w.PdfY,
                    Width      = w.PdfWidth,
                    Height     = w.PdfHeight,
                    NewText    = w.EditedText,
                    FontSize   = w.FontSize,
                }))
            .ToList();

        if (edits.Count == 0)
        {
            StatusText = "No text changes to apply.";
            return;
        }

        var path = _pdfEngine.FilePath;
        var tmp  = Path.GetTempFileName();
        string? undoSnap = null;
        IsBusy = true;
        StatusText = $"Applying {edits.Count} text edit(s)…";
        string? failStatus = null;

        try
        {
            undoSnap = MakeUndoSnapshot();
            await _pdfEngine.CloseAsync();

            var result = await _pdfEditor.BakeTextEditsAsync(path, tmp, edits);
            if (result.IsSuccess)
            {
                File.Move(tmp, path, overwrite: true);
                tmp = null;
                _undoStack.Push(undoSnap);
                undoSnap = null;
                UndoCommand.NotifyCanExecuteChanged();
            }
            else
            {
                failStatus = $"Text edit failed: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplyTextEdits failed");
            failStatus = $"Text edit error: {ex.Message}";
        }
        finally
        {
            try { if (undoSnap != null && File.Exists(undoSnap)) File.Delete(undoSnap); } catch { }
            if (tmp != null && File.Exists(tmp)) File.Delete(tmp);

            IsTextEditMode = false;
            foreach (var p in RenderedPages) { p.IsTextEditModeActive = false; p.EditableWords.Clear(); }

            _renderCts.Cancel();
            _renderCts = new CancellationTokenSource();
            var ct = _renderCts.Token;
            try
            {
                var reopen = await _pdfEngine.OpenAsync(path, ct: ct);
                if (reopen.IsSuccess && reopen.Value != null)
                {
                    DocumentInfo = reopen.Value;
                    TotalPages   = reopen.Value.PageCount;
                    await LoadThumbnailsAsync(ct);
                    await RenderCurrentPagesAsync(ct, finalStatus: failStatus);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogError(ex, "Reopen after text edit failed"); }

            IsBusy = false;
        }
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    private async Task LoadThumbnailsAsync(CancellationToken ct = default)
    {
        Thumbnails.Clear();

        // Add one placeholder per page first so the strip is populated immediately.
        for (var i = 0; i < TotalPages; i++)
            Thumbnails.Add(new ThumbnailItem { PageNumber = i + 1, IsSelected = i == 0 });

        // Render thumbnails one at a time and update each item as it arrives.
        for (var i = 0; i < TotalPages; i++)
        {
            if (ct.IsCancellationRequested || i >= Thumbnails.Count) return;
            try
            {
                var result = await _pdfEngine.RenderThumbnailAsync(i, ct: ct);
                if (!ct.IsCancellationRequested && i < Thumbnails.Count)
                    Thumbnails[i].Thumbnail = result.Value;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to render thumbnail for page {Page}", i + 1);
            }
        }
    }

    private async Task RenderCurrentPagesAsync(CancellationToken ct = default, string? finalStatus = null)
    {
        RenderedPages.Clear();

        // Fetch page dimensions for accurate coordinate mapping
        var pageSizes = new (double W, double H)[TotalPages];
        for (var i = 0; i < TotalPages; i++)
        {
            try
            {
                var info = await _pdfEngine.GetPageAsync(i, ct);
                if (info.IsSuccess && info.Value != null)
                    pageSizes[i] = (info.Value.WidthPoints, info.Value.HeightPoints);
                else
                    pageSizes[i] = (595, 842); // fallback to letter
            }
            catch
            {
                pageSizes[i] = (595, 842);
            }
        }

        // Create all placeholders immediately so the viewer shows document structure at once.
        for (var i = 0; i < TotalPages; i++)
            RenderedPages.Add(new RenderedPageItem
            {
                PageNumber = i + 1,
                PageWidthPoints = pageSizes[i].W,
                PageHeightPoints = pageSizes[i].H,
            });

        // Render each page and update the placeholder when it arrives.
        for (var i = 0; i < TotalPages; i++)
        {
            if (ct.IsCancellationRequested || i >= RenderedPages.Count) return;
            try
            {
                StatusText = $"Rendering page {i + 1} of {TotalPages}…";
                var result = await _pdfEngine.RenderPageAsync(i, dpi: 150, ct: ct);
                if (!ct.IsCancellationRequested && i < RenderedPages.Count)
                    RenderedPages[i].ImageData = result.Value;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to render page {Page}", i + 1);
            }
        }

        if (!ct.IsCancellationRequested)
        {
            StatusText = finalStatus ?? $"{DocumentInfo?.FileName} — {TotalPages} page(s)";

            // Cancel any prior extraction (e.g. from a previous zoom/rotate) and start fresh.
            // We use a dedicated CTS so zoom/rotate re-renders don't kill an in-progress extraction.
            _textExtractCts.Cancel();
            _textExtractCts = new CancellationTokenSource();
            _ = ExtractAllTextLayersAsync(_textExtractCts.Token);
        }
    }

    private async Task ExtractAllTextLayersAsync(CancellationToken ct)
    {
        var totalWords = 0;
        for (var i = 0; i < TotalPages; i++)
        {
            if (ct.IsCancellationRequested || i >= RenderedPages.Count) return;
            try
            {
                var result = await _pdfEngine.ExtractTextAsync(i, ct);
                if (!ct.IsCancellationRequested && i < RenderedPages.Count &&
                    result.IsSuccess && result.Value != null)
                {
                    RenderedPages[i].TextLayer = result.Value;
                    totalWords += result.Value.Count;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _logger.LogWarning(ex, "Text extraction failed for page {Page}", i + 1); }
        }

        if (!ct.IsCancellationRequested)
        {
            var name = DocumentInfo?.FileName ?? "Document";
            StatusText = totalWords > 0
                ? $"{name} — {TotalPages} page(s) — {totalWords} words (drag to select text)"
                : $"{name} — {TotalPages} page(s) — Scanned PDF: no text layer (run OCR for text selection)";
        }
    }
}

public sealed partial class ThumbnailItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int PageNumber { get; init; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private byte[]? _thumbnail;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isSelected;
}

public sealed partial class RenderedPageItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int PageNumber { get; init; }

    /// <summary>Page dimensions in PDF points (1/72 inch).</summary>
    public double PageWidthPoints { get; init; }
    public double PageHeightPoints { get; init; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private byte[]? _imageData;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isTextEditModeActive;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private System.Collections.Generic.IReadOnlyList<PDFAgent.Core.Models.PdfTextSegment>? _textLayer;

    public System.Collections.ObjectModel.ObservableCollection<PDFAgent.App.ViewModels.StickerViewModel>        Stickers        { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<PDFAgent.App.ViewModels.TextAnnotationViewModel> TextAnnotations { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<PDFAgent.App.ViewModels.TextEditWordViewModel>   EditableWords   { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<SearchHighlightRect>                             SearchHighlights { get; } = new();
}

public sealed partial class MergeQueueItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string FilePath { get; }
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public string SubText  => System.IO.Path.GetDirectoryName(FilePath) ?? string.Empty;

    public bool IsWordDocument
    {
        get
        {
            var ext = System.IO.Path.GetExtension(FilePath).ToLowerInvariant();
            return ext is ".doc" or ".docx";
        }
    }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private int _index;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(HasThumbnail))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    private byte[]? _thumbnailBytes;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private int _pageCount;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    private bool _isLoading = true;

    public bool HasThumbnail  => ThumbnailBytes != null;
    public bool ShowPlaceholder => !IsLoading && !HasThumbnail;

    public string PageCountLabel => PageCount > 0 ? $"{PageCount} page{(PageCount == 1 ? "" : "s")}" : "";

    public MergeQueueItem(string filePath, int index)
    {
        FilePath = filePath;
        _index   = index;
    }

    partial void OnPageCountChanged(int value) =>
        OnPropertyChanged(nameof(PageCountLabel));
}

public sealed partial class SplitPreviewPageItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int PageNumber { get; init; }
    public string PageLabel => $"Page {PageNumber}";

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private byte[]? _thumbnailBytes;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isSelected;
}
