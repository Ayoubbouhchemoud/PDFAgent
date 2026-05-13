using System.IO;
using Microsoft.Extensions.Logging;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PDFAgent.PdfEngine;

public sealed class PdfiumEditor : IPdfEditor
{
    private readonly ILogger<PdfiumEditor> _logger;

    public PdfiumEditor(ILogger<PdfiumEditor> logger) => _logger = logger;

    public async Task<OperationResult> MergeAsync(
        IReadOnlyList<string> filePaths, string outputPath, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var output = new PdfDocument();
                foreach (var fp in filePaths)
                {
                    ct.ThrowIfCancellationRequested();
                    using var input = PdfReader.Open(fp, PdfDocumentOpenMode.Import);
                    for (var i = 0; i < input.PageCount; i++)
                        output.AddPage(input.Pages[i]);
                }
                output.Save(outputPath);
                _logger.LogInformation("Merged {Count} PDFs → {Output}", filePaths.Count, outputPath);
                return OperationResult.Ok($"Merged {filePaths.Count} files");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Merge failed");
                return OperationResult.Fail($"Merge failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult> SplitAsync(
        string inputPath, string outputDir, SplitMode mode, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(outputDir);
                using var input = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
                var baseName = Path.GetFileNameWithoutExtension(inputPath);
                for (var i = 0; i < input.PageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    using var pageDoc = new PdfDocument();
                    pageDoc.AddPage(input.Pages[i]);
                    pageDoc.Save(Path.Combine(outputDir, $"{baseName}_page_{i + 1}.pdf"));
                }
                _logger.LogInformation("Split {Input} into {Count} pages", inputPath, input.PageCount);
                return OperationResult.Ok($"Split into {input.PageCount} pages");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Split failed");
                return OperationResult.Fail($"Split failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult> RotatePagesAsync(
        string filePath, IReadOnlyList<int> pageNumbers, int degrees, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var tmp = Path.GetTempFileName();
            try
            {
                var toRotate = new HashSet<int>(pageNumbers);
                using (var input = PdfReader.Open(filePath, PdfDocumentOpenMode.Import))
                using (var output = new PdfDocument())
                {
                    for (var i = 0; i < input.PageCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var page = output.AddPage(input.Pages[i]);
                        if (toRotate.Contains(i))
                            page.Rotate = ((page.Rotate + degrees) % 360 + 360) % 360;
                    }
                    output.Save(tmp);
                }
                File.Move(tmp, filePath, overwrite: true);
                _logger.LogInformation("Rotated {Count} pages by {Deg}°", pageNumbers.Count, degrees);
                return OperationResult.Ok($"Rotated {pageNumbers.Count} pages by {degrees}°");
            }
            catch (Exception ex)
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                _logger.LogError(ex, "Rotate failed");
                return OperationResult.Fail($"Rotate failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult> ExtractPagesAsync(
        string inputPath, string outputPath, IReadOnlyList<int> pageNumbers, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var input = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
                using var output = new PdfDocument();
                foreach (var p in pageNumbers)
                {
                    ct.ThrowIfCancellationRequested();
                    if (p >= 0 && p < input.PageCount)
                        output.AddPage(input.Pages[p]);
                }
                output.Save(outputPath);
                return OperationResult.Ok($"Extracted {pageNumbers.Count} pages");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExtractPages failed");
                return OperationResult.Fail($"Extract failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult> InsertPagesAsync(
        string targetPath, string sourcePath, int insertAtIndex, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var tmp = Path.GetTempFileName();
            try
            {
                using (var target = PdfReader.Open(targetPath, PdfDocumentOpenMode.Import))
                using (var source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import))
                using (var output = new PdfDocument())
                {
                    var clampedIdx = Math.Clamp(insertAtIndex, 0, target.PageCount);
                    for (var i = 0; i < clampedIdx; i++)
                        output.AddPage(target.Pages[i]);
                    for (var i = 0; i < source.PageCount; i++)
                        output.AddPage(source.Pages[i]);
                    for (var i = clampedIdx; i < target.PageCount; i++)
                        output.AddPage(target.Pages[i]);
                    output.Save(tmp);
                }
                File.Move(tmp, targetPath, overwrite: true);
                return OperationResult.Ok($"Inserted {sourcePath} at index {insertAtIndex}");
            }
            catch (Exception ex)
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                _logger.LogError(ex, "InsertPages failed");
                return OperationResult.Fail($"Insert failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult> DeletePagesAsync(
        string filePath, IReadOnlyList<int> pageNumbers, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var tmp = Path.GetTempFileName();
            try
            {
                var toDelete = new HashSet<int>(pageNumbers);
                using (var input = PdfReader.Open(filePath, PdfDocumentOpenMode.Import))
                using (var output = new PdfDocument())
                {
                    for (var i = 0; i < input.PageCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!toDelete.Contains(i))
                            output.AddPage(input.Pages[i]);
                    }
                    output.Save(tmp);
                }
                File.Move(tmp, filePath, overwrite: true);
                return OperationResult.Ok($"Deleted {pageNumbers.Count} pages");
            }
            catch (Exception ex)
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                _logger.LogError(ex, "DeletePages failed");
                return OperationResult.Fail($"Delete failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult> ReorderPagesAsync(
        string filePath, IReadOnlyList<int> newOrder, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var tmp = Path.GetTempFileName();
            try
            {
                using (var input = PdfReader.Open(filePath, PdfDocumentOpenMode.Import))
                using (var output = new PdfDocument())
                {
                    foreach (var idx in newOrder)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (idx >= 0 && idx < input.PageCount)
                            output.AddPage(input.Pages[idx]);
                    }
                    output.Save(tmp);
                }
                File.Move(tmp, filePath, overwrite: true);
                return OperationResult.Ok("Pages reordered");
            }
            catch (Exception ex)
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                _logger.LogError(ex, "ReorderPages failed");
                return OperationResult.Fail($"Reorder failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult> AddWatermarkAsync(
        string filePath, string outputPath, string text, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var input = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                using var output = new PdfDocument();
                var font = new XFont("Arial", 72, XFontStyleEx.Bold);
                var brush = new XSolidBrush(XColor.FromArgb(64, 180, 180, 180));

                for (var i = 0; i < input.PageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = output.AddPage(input.Pages[i]);
                    using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    var state = gfx.Save();
                    gfx.TranslateTransform(page.Width.Point / 2, page.Height.Point / 2);
                    gfx.RotateTransform(-45);
                    var size = gfx.MeasureString(text, font);
                    gfx.DrawString(text, font, brush,
                        new XPoint(-size.Width / 2, size.Height / 4));
                    gfx.Restore(state);
                }
                output.Save(outputPath);
                return OperationResult.Ok($"Added watermark '{text}'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddWatermark failed");
                return OperationResult.Fail($"Watermark failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult> AddStampAsync(
        string filePath, string outputPath, string stampText, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var input = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                using var output = new PdfDocument();
                var font = new XFont("Arial", 32, XFontStyleEx.Bold);
                var pen = new XPen(XColor.FromArgb(255, 200, 0, 0), 2.5);
                var textBrush = new XSolidBrush(XColor.FromArgb(255, 200, 0, 0));

                for (var i = 0; i < input.PageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = output.AddPage(input.Pages[i]);
                    using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    var size = gfx.MeasureString(stampText, font);
                    const double pad = 8;
                    double x = page.Width.Point - size.Width - pad * 2 - 24;
                    const double y = 24;
                    gfx.DrawRectangle(pen, x, y, size.Width + pad * 2, size.Height + pad * 2);
                    gfx.DrawString(stampText, font, textBrush,
                        new XPoint(x + pad, y + pad + size.Height * 0.78));
                }
                output.Save(outputPath);
                return OperationResult.Ok($"Added stamp '{stampText}'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddStamp failed");
                return OperationResult.Fail($"Stamp failed: {ex.Message}");
            }
        }, ct);
    }
}
