using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;
using PdfiumViewer;

namespace PDFAgent.PdfEngine;

public sealed class PdfiumEngine : IPdfEngine
{
    private readonly ILogger<PdfiumEngine> _logger;
    private PdfDocument? _document;
    private PdfDocumentInfo? _documentInfo;
    private bool _disposed;

    public string FilePath { get; private set; } = string.Empty;
    public bool IsOpen => _document != null;
    public PdfDocumentInfo? DocumentInfo => _documentInfo;

    public PdfiumEngine(ILogger<PdfiumEngine> logger)
    {
        _logger = logger;
    }

    public async Task<OperationResult<PdfDocumentInfo>> OpenAsync(
        string filePath, string? password = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return OperationResult.Fail<PdfDocumentInfo>($"File not found: {filePath}");

                _document = password != null
                    ? PdfDocument.Load(filePath, password)
                    : PdfDocument.Load(filePath);

                FilePath = filePath;
                _documentInfo = ExtractDocumentInfo();
                _logger.LogInformation("Opened PDF: {Path}, {Pages} pages", filePath, _documentInfo.PageCount);

                return OperationResult.Ok(_documentInfo);
            }
            catch (Exception ex) when (ex.Message.Contains("password") || ex.Message.Contains("Password"))
            {
                return OperationResult.Fail<PdfDocumentInfo>(
                    "Document is password-protected.", "ENCRYPTED", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open PDF: {Path}", filePath);
                return OperationResult.Fail<PdfDocumentInfo>(
                    $"Failed to open PDF: {ex.Message}", "OPEN_FAILED", ex);
            }
        }, ct);
    }

    public Task<OperationResult> CloseAsync()
    {
        _document?.Dispose();
        _document = null;
        _documentInfo = null;
        FilePath = string.Empty;
        return Task.FromResult(OperationResult.Ok());
    }

    public async Task<OperationResult<PdfPageInfo>> GetPageAsync(
        int pageNumber, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (_document == null)
                    return OperationResult.Fail<PdfPageInfo>("No document open");

                if (pageNumber < 0 || pageNumber >= _document.PageCount)
                    return OperationResult.Fail<PdfPageInfo>($"Invalid page number: {pageNumber}");

                var width = _document.PageSizes[pageNumber].Width;
                var height = _document.PageSizes[pageNumber].Height;

                return OperationResult.Ok(new PdfPageInfo
                {
                    PageNumber = pageNumber + 1,
                    WidthPoints = width,
                    HeightPoints = height,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get page {Page}", pageNumber);
                return OperationResult.Fail<PdfPageInfo>($"Failed to get page: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult<IReadOnlyList<PdfPageInfo>>> GetPagesAsync(
        CancellationToken ct = default)
    {
        if (_document == null)
            return OperationResult.Fail<IReadOnlyList<PdfPageInfo>>("No document open");

        var pages = new List<PdfPageInfo>(_document.PageCount);
        for (var i = 0; i < _document.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await GetPageAsync(i, ct);
            if (result.IsSuccess && result.Value != null)
                pages.Add(result.Value);
        }

        return OperationResult.Ok<IReadOnlyList<PdfPageInfo>>(pages);
    }

    public async Task<OperationResult<byte[]>> RenderPageAsync(
        int pageNumber, double dpi = 150, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (_document == null)
                    return OperationResult.Fail<byte[]>("No document open");

                var pageSize = _document.PageSizes[pageNumber];
                var scale = dpi / 72.0;
                var width = (int)(pageSize.Width * scale);
                var height = (int)(pageSize.Height * scale);

                using var image = _document.Render(pageNumber, width, height, (float)dpi, (float)dpi, false);
                // PdfiumViewer returns a Bitmap with default 96 DPI metadata regardless of
                // the render DPI. Stamp the correct DPI so WPF displays the image at the
                // right physical size (816 DIPs wide for a letter page at 150 DPI = 72/96 ratio).
                if (image is System.Drawing.Bitmap bmp)
                    bmp.SetResolution((float)dpi, (float)dpi);
                using var ms = new MemoryStream();
                image.Save(ms, ImageFormat.Png);
                return OperationResult.Ok(ms.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to render page {Page}", pageNumber);
                return OperationResult.Fail<byte[]>($"Render failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult<byte[]>> RenderThumbnailAsync(
        int pageNumber, int maxSize = 256, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (_document == null)
                    return OperationResult.Fail<byte[]>("No document open");

                var size = _document.PageSizes[pageNumber];
                var scale = (float)Math.Min(maxSize / size.Width, maxSize / size.Height);
                var w = (int)(size.Width * scale);
                var h = (int)(size.Height * scale);

                using var image = _document.Render(pageNumber, w, h, 72f, 72f, false);
                using var ms = new MemoryStream();
                image.Save(ms, ImageFormat.Png);
                return OperationResult.Ok(ms.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to render thumbnail for page {Page}", pageNumber);
                return OperationResult.Fail<byte[]>($"Thumbnail failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult<IReadOnlyList<PdfTextSegment>>> ExtractTextAsync(
        int pageNumber, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (_document == null || string.IsNullOrEmpty(FilePath))
                    return OperationResult.Fail<IReadOnlyList<PdfTextSegment>>("No document open");

                var words = PdfiumTextNative.ExtractWords(FilePath, pageNumber);
                return OperationResult.Ok<IReadOnlyList<PdfTextSegment>>(words);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text from page {Page}", pageNumber);
                return OperationResult.Fail<IReadOnlyList<PdfTextSegment>>($"Text extraction failed: {ex.Message}");
            }
        }, ct);
    }

    /// <summary>
    /// Returns page text using PdfiumViewer's already-open document handle.
    /// This is the reliable path for search: no file re-open, no P/Invoke path issues.
    /// </summary>
    public async Task<OperationResult<string>> GetPageTextAsync(
        int pageNumber, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (_document == null)
                    return OperationResult.Fail<string>("No document open");
                if (pageNumber < 0 || pageNumber >= _document.PageCount)
                    return OperationResult.Fail<string>($"Page {pageNumber} out of range");

                var text = _document.GetPdfText(pageNumber) ?? string.Empty;
                return OperationResult.Ok<string>(text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get page text for page {Page}", pageNumber);
                return OperationResult.Fail<string>($"GetPageText failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<(byte[]? Thumbnail, int PageCount)> RenderExternalPreviewAsync(
        string filePath, int maxSize = 220, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var doc  = PdfDocument.Load(filePath);
                var size       = doc.PageSizes[0];
                var scale      = (float)Math.Min(maxSize / size.Width, maxSize / size.Height);
                var w          = Math.Max(1, (int)(size.Width  * scale));
                var h          = Math.Max(1, (int)(size.Height * scale));
                using var img  = doc.Render(0, w, h, 72f, 72f, false);
                using var ms   = new MemoryStream();
                img.Save(ms, ImageFormat.Png);
                return ((byte[]?)ms.ToArray(), doc.PageCount);
            }
            catch
            {
                return (null, 0);
            }
        }, ct);
    }

    private PdfDocumentInfo ExtractDocumentInfo()
    {
        if (_document == null) throw new InvalidOperationException("No document open");

        var fi = new FileInfo(FilePath);
        return new PdfDocumentInfo
        {
            FilePath = FilePath,
            FileSizeBytes = fi.Length,
            PageCount = _document.PageCount,
            PdfVersion = "1.x",
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await CloseAsync();
        _disposed = true;
    }
}
