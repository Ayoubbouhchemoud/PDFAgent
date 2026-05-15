using System.IO;
using System.Text;
using System.Text.Json;
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
            var tempFiles = new List<string>();
            try
            {
                // Resolve Word documents to temporary PDFs first.
                var pdfPaths = new List<string>(filePaths.Count);
                foreach (var fp in filePaths)
                {
                    ct.ThrowIfCancellationRequested();
                    var ext = Path.GetExtension(fp).ToLowerInvariant();
                    if (ext is ".doc" or ".docx")
                    {
                        var converted = WordConverter.ConvertToPdf(fp);
                        if (converted == null)
                            return OperationResult.Fail(
                                $"Could not convert '{Path.GetFileName(fp)}'. " +
                                "Ensure Microsoft Word is installed.");
                        tempFiles.Add(converted);
                        pdfPaths.Add(converted);
                    }
                    else
                    {
                        pdfPaths.Add(fp);
                    }
                }

                ct.ThrowIfCancellationRequested();

                // Use PDFium directly — handles all PDF versions including
                // cross-reference streams that PdfSharp cannot import.
                PdfiumMergeNative.MergeFiles(pdfPaths, outputPath);

                _logger.LogInformation("Merged {Count} files → {Output}", filePaths.Count, outputPath);
                return OperationResult.Ok($"Merged {filePaths.Count} files");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Merge failed");
                return OperationResult.Fail($"Merge failed: {ex.Message}");
            }
            finally
            {
                foreach (var f in tempFiles)
                    try { File.Delete(f); } catch { }
            }
        }, ct);
    }

    public async Task<OperationResult> SplitAsync(
        string inputPath, SplitOptions opts, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var input = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);

                switch (opts.Mode)
                {
                    case SplitMode.SplitAll:
                    {
                        Directory.CreateDirectory(opts.OutputDir);
                        for (var i = 0; i < input.PageCount; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            using var doc = new PdfDocument();
                            doc.AddPage(input.Pages[i]);
                            doc.Save(Path.Combine(opts.OutputDir, $"{opts.BaseName}_page_{i + 1}.pdf"));
                        }
                        _logger.LogInformation("SplitAll {Input} → {Count} files", inputPath, input.PageCount);
                        return OperationResult.Ok($"Split into {input.PageCount} individual pages");
                    }

                    case SplitMode.SplitRange:
                    {
                        using var doc = new PdfDocument();
                        var added = 0;
                        foreach (var idx in opts.PageIndices)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (idx >= 0 && idx < input.PageCount)
                            {
                                doc.AddPage(input.Pages[idx]);
                                added++;
                            }
                        }
                        if (added == 0)
                            return OperationResult.Fail("No valid pages in the specified range");
                        doc.Save(opts.OutputFile);
                        _logger.LogInformation("SplitRange {Input} → {File} ({Count} pages)", inputPath, opts.OutputFile, added);
                        return OperationResult.Ok($"Extracted {added} page(s) → {Path.GetFileName(opts.OutputFile)}");
                    }

                    case SplitMode.SplitEvery:
                    {
                        Directory.CreateDirectory(opts.OutputDir);
                        var n      = Math.Max(1, opts.EveryN);
                        var chunks = (int)Math.Ceiling(input.PageCount / (double)n);
                        for (var chunk = 0; chunk < chunks; chunk++)
                        {
                            ct.ThrowIfCancellationRequested();
                            using var doc   = new PdfDocument();
                            var firstPage   = chunk * n + 1;
                            var lastPage    = Math.Min(firstPage + n - 1, input.PageCount);
                            for (var i = firstPage - 1; i < lastPage; i++)
                                doc.AddPage(input.Pages[i]);
                            var fileName = $"{opts.BaseName}_pages_{firstPage}-{lastPage}.pdf";
                            doc.Save(Path.Combine(opts.OutputDir, fileName));
                        }
                        _logger.LogInformation("SplitEvery{N} {Input} → {Chunks} files", n, inputPath, chunks);
                        return OperationResult.Ok($"Split into {chunks} file(s) of up to {n} page(s) each");
                    }

                    default:
                        return OperationResult.Fail($"Unsupported split mode: {opts.Mode}");
                }
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

    public async Task<OperationResult> AddPageAnnotationAsync(
        string filePath, string outputPath, int pageNumber, string text, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var input = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                using var output = new PdfDocument();
                var font = new XFont("Arial", 11, XFontStyleEx.Regular);
                var labelFont = new XFont("Arial", 9, XFontStyleEx.Bold);
                var fillBrush = new XSolidBrush(XColor.FromArgb(230, 255, 255, 180));
                var borderPen = new XPen(XColor.FromArgb(255, 180, 140, 0), 1.2);
                var textBrush = new XSolidBrush(XColor.FromArgb(255, 60, 40, 0));
                var targetIdx = Math.Clamp(pageNumber - 1, 0, input.PageCount - 1);

                for (var i = 0; i < input.PageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = output.AddPage(input.Pages[i]);
                    if (i != targetIdx) continue;

                    using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    const double pad = 6;
                    const double boxW = 220;
                    const double boxH = 42;
                    double x = page.Width.Point - boxW - 12;
                    double y = 12;
                    gfx.DrawRectangle(fillBrush, x, y, boxW, boxH);
                    gfx.DrawRectangle(borderPen, x, y, boxW, boxH);
                    gfx.DrawString("ANNOTATION", labelFont, textBrush, new XPoint(x + pad, y + pad + 9));
                    var wrapped = text.Length > 40 ? text[..40] + "…" : text;
                    gfx.DrawString(wrapped, font, textBrush, new XPoint(x + pad, y + pad + 24));
                }

                output.Save(outputPath);
                _logger.LogInformation("Annotation added to page {Page} → {Output}", pageNumber, outputPath);
                return OperationResult.Ok($"Annotation added to page {pageNumber}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddPageAnnotation failed");
                return OperationResult.Fail($"Annotation failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult> AddSignatureImageAsync(
        string filePath, string outputPath, SignatureOverlayOptions opts, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var input = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                using var output = new PdfDocument();
                var targetIdx = Math.Clamp(opts.PageNumber - 1, 0, input.PageCount - 1);

                for (var i = 0; i < input.PageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = output.AddPage(input.Pages[i]);
                    if (i != targetIdx) continue;

                    using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    double pw = page.Width.Point;
                    double ph = page.Height.Point;
                    double m  = opts.Margin;
                    double sigW = opts.SignatureWidth;
                    double sigH = opts.SignatureHeight;

                    double x, y;
                    if (opts.AbsoluteX.HasValue && opts.AbsoluteY.HasValue)
                    {
                        // Sticker drag-and-drop path: position is already in PDF points.
                        x = Math.Clamp(opts.AbsoluteX.Value, 0, Math.Max(0, pw - sigW));
                        y = Math.Clamp(opts.AbsoluteY.Value, 0, Math.Max(0, ph - sigH));
                    }
                    else if (opts.CustomX.HasValue && opts.CustomY.HasValue)
                    {
                        // Legacy click-to-place: normalised 0-1 fraction, signature centred.
                        x = Math.Clamp(opts.CustomX.Value * pw - sigW / 2, 0, pw - sigW);
                        y = Math.Clamp(opts.CustomY.Value * ph - sigH / 2, 0, ph - sigH);
                    }
                    else
                    {
                        x = opts.Placement switch
                        {
                            SignaturePlacement.BottomLeft   => m,
                            SignaturePlacement.BottomCenter => (pw - sigW) / 2,
                            _                              => pw - sigW - m,
                        };
                        y = ph - sigH - m;
                    }

                    // Leading '{' means drawn-signature vector JSON; anything else is a PNG.
                    if (opts.ImageBytes.Length > 0 && opts.ImageBytes[0] == (byte)'{')
                        DrawVectorSignature(gfx, opts.ImageBytes, x, y, sigW, sigH);
                    else
                        DrawImageSignature(gfx, opts.ImageBytes, x, y, sigW, sigH);
                }

                output.Save(outputPath);
                _logger.LogInformation("Signature applied to page {Page} → {Output}", opts.PageNumber, outputPath);
                return OperationResult.Ok("Signature applied");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddSignatureImage failed");
                return OperationResult.Fail($"Signature failed: {ex.Message}");
            }
        }, ct);
    }

    // Draw uploaded-image signature (PNG with alpha channel).
    private static void DrawImageSignature(XGraphics gfx, byte[] pngBytes, double x, double y, double w, double h)
    {
        using var ms = new MemoryStream(pngBytes);
        var xImage = XImage.FromStream(ms);
        gfx.DrawImage(xImage, x, y, w, h);
    }

    // Draw vector signature from JSON: {"t":"v","w":<cw>,"h":<ch>,"s":[[[x,y],...], ...]}
    private static void DrawVectorSignature(XGraphics gfx, byte[] jsonBytes, double x, double y, double w, double h)
    {
        using var doc = JsonDocument.Parse(jsonBytes);
        var root = doc.RootElement;

        // Parse strokes
        var strokes = new List<(double X, double Y)[]>();
        foreach (var strokeEl in root.GetProperty("s").EnumerateArray())
        {
            var pts = strokeEl.EnumerateArray()
                .Select(pt =>
                {
                    var coords = pt.EnumerateArray().ToArray();
                    return (coords[0].GetDouble(), coords[1].GetDouble());
                })
                .ToArray();
            if (pts.Length >= 2)
                strokes.Add(pts);
        }

        if (strokes.Count == 0) return;

        // Tight bounding box
        var allPts = strokes.SelectMany(s => s).ToArray();
        double minX = allPts.Min(p => p.X);
        double minY = allPts.Min(p => p.Y);
        double maxX = allPts.Max(p => p.X);
        double maxY = allPts.Max(p => p.Y);
        double bboxW = Math.Max(maxX - minX, 1);
        double bboxH = Math.Max(maxY - minY, 1);

        // Uniform scale with 5 % padding, centred in the box
        const double pad = 0.05;
        double scale = Math.Min(w * (1 - 2 * pad) / bboxW, h * (1 - 2 * pad) / bboxH);
        double ox = x + (w - bboxW * scale) / 2;
        double oy = y + (h - bboxH * scale) / 2;

        // Pen width ≈ 2 pts (matches 2.5 px on 96 dpi canvas)
        var pen = new XPen(XColors.Black, 2.0)
        {
            LineCap  = XLineCap.Round,
            LineJoin = XLineJoin.Round,
        };

        foreach (var stroke in strokes)
        {
            var xPts = stroke
                .Select(p => new XPoint(ox + (p.X - minX) * scale, oy + (p.Y - minY) * scale))
                .ToArray();

            var path = new XGraphicsPath();
            path.AddLines(xPts);
            gfx.DrawPath(pen, path);
        }
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

    public async Task<OperationResult> BakeTextAnnotationsAsync(
        string filePath, string outputPath,
        IReadOnlyList<PDFAgent.Core.Models.TextAnnotationRecord> annotations,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var input = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                using var output = new PdfDocument();

                var byPage = annotations
                    .GroupBy(a => a.PageNumber - 1)
                    .ToDictionary(g => g.Key, g => g.ToList());

                for (var i = 0; i < input.PageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = output.AddPage(input.Pages[i]);
                    if (!byPage.TryGetValue(i, out var list)) continue;

                    using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    foreach (var ann in list)
                    {
                        if (string.IsNullOrWhiteSpace(ann.Text)) continue;
                        var font  = new XFont("Arial", ann.FontSize, XFontStyleEx.Regular);
                        var rect  = new XRect(ann.X, ann.Y, ann.Width, ann.Height);
                        // White background so text is legible over existing content.
                        gfx.DrawRectangle(XBrushes.White, rect);
                        gfx.DrawString(ann.Text, font, XBrushes.Black, rect, XStringFormats.TopLeft);
                    }
                }

                output.Save(outputPath);
                _logger.LogInformation("Baked {Count} text annotations → {Output}", annotations.Count, outputPath);
                return OperationResult.Ok($"Baked {annotations.Count} text annotation(s)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BakeTextAnnotations failed");
                return OperationResult.Fail($"Text bake failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult> BakeTextEditsAsync(
        string filePath, string outputPath,
        IReadOnlyList<PDFAgent.Core.Models.TextEditRecord> edits,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var input  = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                using var output = new PdfDocument();

                var byPage = edits
                    .GroupBy(e => e.PageNumber - 1)
                    .ToDictionary(g => g.Key, g => g.ToList());

                for (var i = 0; i < input.PageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = output.AddPage(input.Pages[i]);
                    if (!byPage.TryGetValue(i, out var list)) continue;

                    using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    foreach (var edit in list)
                    {
                        if (string.IsNullOrWhiteSpace(edit.NewText)) continue;

                        // White-out the original word area; extend width to accommodate longer text
                        var whiteoutRect = new XRect(edit.X, edit.Y, edit.Width * 2.5, edit.Height * 1.2);
                        gfx.DrawRectangle(XBrushes.White, whiteoutRect);

                        // Draw the replacement text
                        var font     = new XFont("Arial", Math.Max(edit.FontSize, 6), XFontStyleEx.Regular);
                        var textRect = new XRect(edit.X, edit.Y, page.Width.Point - edit.X, edit.Height * 1.5);
                        gfx.DrawString(edit.NewText, font, XBrushes.Black, textRect, XStringFormats.TopLeft);
                    }
                }

                output.Save(outputPath);
                _logger.LogInformation("Baked {Count} text edits → {Output}", edits.Count, outputPath);
                return OperationResult.Ok($"Applied {edits.Count} text edit(s)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BakeTextEdits failed");
                return OperationResult.Fail($"Text edit bake failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult> AddBlankPageAsync(
        string filePath, string outputPath,
        int insertAtIndex, double widthPts, double heightPts,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var input  = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                using var output = new PdfDocument();

                var clampedIdx = Math.Clamp(insertAtIndex, 0, input.PageCount);

                for (var i = 0; i < clampedIdx; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    output.AddPage(input.Pages[i]);
                }

                var blank  = output.AddPage();
                blank.Width  = XUnit.FromPoint(widthPts);
                blank.Height = XUnit.FromPoint(heightPts);

                for (var i = clampedIdx; i < input.PageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    output.AddPage(input.Pages[i]);
                }

                output.Save(outputPath);
                _logger.LogInformation("Added blank page at index {Idx} → {Output}", clampedIdx, outputPath);
                return OperationResult.Ok($"Blank page inserted at position {clampedIdx + 1}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddBlankPage failed");
                return OperationResult.Fail($"Add page failed: {ex.Message}");
            }
        }, ct);
    }
}
