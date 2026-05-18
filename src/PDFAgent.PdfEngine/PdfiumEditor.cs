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
            try
            {
                ct.ThrowIfCancellationRequested();

                // Use PDFium directly — handles all PDF versions including
                // cross-reference streams that PdfSharp cannot import.
                PdfiumMergeNative.MergeFiles(filePaths, outputPath);

                _logger.LogInformation("Merged {Count} files → {Output}", filePaths.Count, outputPath);
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

                        // Cover exactly the original glyph bounding box (1 pt margin on all sides
                        // to absorb any sub-pixel rounding in the PDFium coordinates).
                        var whiteout = new XRect(edit.X - 1, edit.Y - 1, edit.Width + 2, edit.Height + 2);
                        gfx.DrawRectangle(XBrushes.White, whiteout);

                        // Recreate the font as close as possible to the original.
                        var font = CreateEditFont(edit.FontName, edit.FontSize, edit.IsBold, edit.IsItalic);
                        var textRect = new XRect(edit.X, edit.Y, page.Width.Point - edit.X, edit.Height + 4);
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

    public async Task<OperationResult> BakeDrawingsAsync(
        string filePath, string outputPath,
        IReadOnlyList<PDFAgent.Core.Models.DrawingStroke> strokes,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var input  = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                using var output = new PdfDocument();

                var byPage = strokes
                    .Where(s => s.Points.Count >= 1)
                    .GroupBy(s => s.PageNumber - 1)
                    .ToDictionary(g => g.Key, g => g.ToList());

                for (var i = 0; i < input.PageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = output.AddPage(input.Pages[i]);
                    if (!byPage.TryGetValue(i, out var pageStrokes)) continue;

                    using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                    foreach (var stroke in pageStrokes)
                    {
                        var color = XColor.FromArgb(stroke.A, stroke.R, stroke.G, stroke.B);
                        var pen   = new XPen(color, Math.Max(stroke.Thickness, 0.5))
                        {
                            LineCap  = XLineCap.Round,
                            LineJoin = XLineJoin.Round,
                        };

                        if (stroke.Points.Count == 1)
                        {
                            // Single-point tap: draw a small filled circle
                            var p = stroke.Points[0];
                            double r = stroke.Thickness / 2.0;
                            gfx.DrawEllipse(new XSolidBrush(color),
                                p.X - r, p.Y - r, r * 2, r * 2);
                            continue;
                        }

                        // Multi-point: draw as a smooth polyline path
                        var path  = new XGraphicsPath();
                        var xpts  = stroke.Points
                            .Select(p => new XPoint(p.X, p.Y))
                            .ToArray();
                        path.AddLines(xpts);
                        gfx.DrawPath(pen, path);
                    }
                }

                output.Save(outputPath);
                _logger.LogInformation("BakeDrawings {Count} strokes → {Output}", strokes.Count, outputPath);
                return OperationResult.Ok($"Applied {strokes.Count} ink stroke(s)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BakeDrawings failed");
                return OperationResult.Fail($"Drawing bake failed: {ex.Message}");
            }
        }, ct);
    }

    private static XFont CreateEditFont(string? rawName, double fontSize, bool bold, bool italic)
    {
        var size  = Math.Max(fontSize, 6);
        var style = bold && italic ? XFontStyleEx.BoldItalic
                  : bold          ? XFontStyleEx.Bold
                  : italic        ? XFontStyleEx.Italic
                                  : XFontStyleEx.Regular;

        var family = CleanFontFamily(rawName, ref bold, ref italic);

        try { return new XFont(family, size, style); }
        catch
        {
            try { return new XFont("Arial", size, style); }
            catch { return new XFont("Arial", size, XFontStyleEx.Regular); }
        }
    }

    private static string CleanFontFamily(string? raw, ref bool bold, ref bool italic)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Arial";

        // Strip PDF subset prefix "XXXXXX+FontName"
        var idx = raw.IndexOf('+');
        var name = idx >= 0 ? raw[(idx + 1)..] : raw;

        // Detect style modifiers embedded in the name
        var lower = name.ToLowerInvariant();
        if (lower.Contains("bold"))                             bold   = true;
        if (lower.Contains("italic") || lower.Contains("oblique")) italic = true;

        // Remove trailing style and foundry suffixes so we get a clean family name
        name = System.Text.RegularExpressions.Regex.Replace(
            name,
            @"[,\-]?(BoldItalic|Bold|Italic|Oblique|Regular|Roman|Light|Thin|Medium|SemiBold|Black|Narrow|Condensed|MT|PS|PST)$",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim(['-', ',', ' ']);

        // Map common PDF logical font names to installed Windows font families
        return name switch
        {
            "Helvetica"      => "Arial",
            "CourierNew"     => "Courier New",
            "Courier"        => "Courier New",
            "TimesNewRoman"  => "Times New Roman",
            "Times"          => "Times New Roman",
            "Symbol"         => "Symbol",
            "ZapfDingbats"   => "Wingdings",
            _ when name.Length < 2 => "Arial",
            _                => name,
        };
    }

    public async Task<OperationResult> CompressAsync(
        string inputPath,
        string outputPath,
        int? imageDpi,
        int jpegQuality,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            // All XImage streams must outlive output.Save() — PdfSharp holds internal stream refs.
            var keepAlive = new List<MemoryStream>();
            try
            {
                if (imageDpi == null)
                {
                    // Lossless: re-save through PdfSharp with FlateDecode on every content stream.
                    // Preserves text, fonts, and vector graphics. Best for uncompressed source PDFs.
                    using var input  = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
                    using var output = new PdfDocument();
                    output.Options.CompressContentStreams = true;

                    for (var i = 0; i < input.PageCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        output.AddPage(input.Pages[i]);
                        progress?.Report((i + 1.0) / input.PageCount * 0.95);
                    }
                    output.Save(outputPath);
                }
                else
                {
                    // Image-based: render each page as a JPEG at the requested DPI and quality.
                    // The text layer is replaced by raster pixels.
                    var dpi     = Math.Clamp(imageDpi.Value, 36, 600);
                    var quality = (long)Math.Clamp(jpegQuality, 1, 100);

                    var jpegCodec = System.Drawing.Imaging.ImageCodecInfo
                        .GetImageDecoders()
                        .First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                    var encParams = new System.Drawing.Imaging.EncoderParameters(1);
                    encParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                        System.Drawing.Imaging.Encoder.Quality, quality);

                    using var pdfIn  = PdfiumViewer.PdfDocument.Load(inputPath);
                    using var output = new PdfDocument();

                    for (var i = 0; i < pdfIn.PageCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();

                        var sz    = pdfIn.PageSizes[i];
                        var scale = dpi / 72.0;
                        var w     = Math.Max(1, (int)(sz.Width  * scale));
                        var h     = Math.Max(1, (int)(sz.Height * scale));

                        using var bmp = (System.Drawing.Bitmap)pdfIn.Render(
                            i, w, h, (float)dpi, (float)dpi, false);

                        var ms = new MemoryStream();
                        bmp.Save(ms, jpegCodec, encParams);
                        ms.Position = 0;
                        keepAlive.Add(ms); // must stay alive until output.Save

                        var page    = output.AddPage();
                        page.Width  = XUnit.FromPoint(sz.Width);
                        page.Height = XUnit.FromPoint(sz.Height);

                        var xImg = XImage.FromStream(ms);
                        using var gfx = XGraphics.FromPdfPage(page);
                        gfx.DrawImage(xImg, 0, 0, page.Width.Point, page.Height.Point);

                        progress?.Report((i + 1.0) / pdfIn.PageCount * 0.95);
                    }
                    output.Save(outputPath);
                }

                progress?.Report(1.0);

                var origSize = new FileInfo(inputPath).Length;
                var newSize  = new FileInfo(outputPath).Length;
                var savedPct = origSize > 0 ? (1.0 - (double)newSize / origSize) * 100.0 : 0;
                _logger.LogInformation(
                    "Compress dpi={Dpi} q={Q}: {Orig}→{New} bytes (−{Pct:N1}%)",
                    imageDpi?.ToString() ?? "lossless", jpegQuality, origSize, newSize, savedPct);

                return OperationResult.Ok(
                    $"{FormatBytes(origSize)} → {FormatBytes(newSize)} (−{savedPct:N1}%)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CompressPdf failed");
                if (File.Exists(outputPath)) try { File.Delete(outputPath); } catch { }
                return OperationResult.Fail($"Compression failed: {ex.Message}");
            }
            finally
            {
                foreach (var s in keepAlive) try { s.Dispose(); } catch { }
            }
        }, ct);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:N1} MB";
        if (bytes >= 1_024)     return $"{bytes / 1_024.0:N0} KB";
        return $"{bytes} B";
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

    // ── Convert to PDF ───────────────────────────────────────────────────────

    private static readonly HashSet<string> _imageExts =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif" };
    public async Task<OperationResult> ConvertToPdfAsync(
        string inputPath, string outputPath, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();

        if (_imageExts.Contains(ext))
            return await ConvertImagesToPdfAsync(new[] { inputPath }, outputPath, null, ct);

        if (ext == ".txt")
            return await ConvertTextToPdfAsync(inputPath, outputPath, ct);

        return OperationResult.Fail($"Unsupported file type: {ext}");
    }

    public async Task<OperationResult> ConvertImagesToPdfAsync(
        IReadOnlyList<string> imagePaths, string outputPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var keepAlive = new List<MemoryStream>();
            try
            {
                using var output = new PdfDocument();

                for (var i = 0; i < imagePaths.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    using var bmp = new System.Drawing.Bitmap(imagePaths[i]);

                    var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    keepAlive.Add(ms);

                    var page = output.AddPage();
                    var xImg = XImage.FromStream(ms);
                    // Keep original aspect ratio; use 96 dpi as canonical screen resolution
                    page.Width  = XUnit.FromPoint(xImg.PixelWidth  * 72.0 / Math.Max(1, xImg.HorizontalResolution));
                    page.Height = XUnit.FromPoint(xImg.PixelHeight * 72.0 / Math.Max(1, xImg.VerticalResolution));

                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawImage(xImg, 0, 0, page.Width.Point, page.Height.Point);

                    progress?.Report((i + 1.0) / imagePaths.Count);
                }

                output.Save(outputPath);
                return OperationResult.Ok($"Converted {imagePaths.Count} image(s) → {Path.GetFileName(outputPath)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConvertImages failed");
                if (File.Exists(outputPath)) try { File.Delete(outputPath); } catch { }
                return OperationResult.Fail($"Image conversion failed: {ex.Message}");
            }
            finally
            {
                foreach (var s in keepAlive) try { s.Dispose(); } catch { }
            }
        }, ct);
    }

    private Task<OperationResult> ConvertTextToPdfAsync(
        string inputPath, string outputPath, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var lines = File.ReadAllLines(inputPath, System.Text.Encoding.UTF8);
                using var output = new PdfDocument();

                const double marginPt    = 56.7; // 2 cm
                const double fontSizePt  = 11;
                const double lineHeightPt = fontSizePt * 1.4;
                var font = new XFont("Courier New", fontSizePt);

                // A4 page
                const double pageWidthPt  = 595.28;
                const double pageHeightPt = 841.89;
                var usableWidth  = pageWidthPt  - 2 * marginPt;
                var usableHeight = pageHeightPt - 2 * marginPt;
                var linesPerPage = (int)(usableHeight / lineHeightPt);

                PdfPage? page = null;
                XGraphics? gfx = null;
                var lineOnPage = 0;

                foreach (var rawLine in lines)
                {
                    ct.ThrowIfCancellationRequested();

                    // Word-wrap long lines
                    var wrapped = WrapLine(rawLine, font, usableWidth);
                    foreach (var subLine in wrapped)
                    {
                        if (page == null || lineOnPage >= linesPerPage)
                        {
                            gfx?.Dispose();
                            page = output.AddPage();
                            page.Width  = XUnit.FromPoint(pageWidthPt);
                            page.Height = XUnit.FromPoint(pageHeightPt);
                            gfx = XGraphics.FromPdfPage(page);
                            lineOnPage = 0;
                        }

                        var y = marginPt + lineOnPage * lineHeightPt + fontSizePt;
                        gfx!.DrawString(subLine, font, XBrushes.Black,
                            new XRect(marginPt, y, usableWidth, lineHeightPt),
                            XStringFormats.TopLeft);
                        lineOnPage++;
                    }
                }
                gfx?.Dispose();

                output.Save(outputPath);
                _logger.LogInformation("TXT→PDF: {In} → {Out}", inputPath, outputPath);
                return OperationResult.Ok($"Converted {Path.GetFileName(inputPath)} → {Path.GetFileName(outputPath)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConvertText failed");
                if (File.Exists(outputPath)) try { File.Delete(outputPath); } catch { }
                return OperationResult.Fail($"Text conversion failed: {ex.Message}");
            }
        }, ct);
    }

    private static IEnumerable<string> WrapLine(string line, XFont font, double maxWidth)
    {
        if (string.IsNullOrEmpty(line)) { yield return string.Empty; yield break; }

        // Rough estimate: measure using a throwaway graphics context
        // XGraphics measurement requires a real context; use char-count heuristic instead
        // (Courier New is monospace — all chars same width)
        var charWidth = maxWidth / 85; // approx chars at 11pt Courier, 470pt wide
        var maxChars  = Math.Max(10, (int)(maxWidth / charWidth));

        var remaining = line;
        while (remaining.Length > maxChars)
        {
            var split = remaining.LastIndexOf(' ', maxChars);
            if (split <= 0) split = maxChars;
            yield return remaining[..split];
            remaining = remaining[split..].TrimStart();
        }
        yield return remaining;
    }
}
