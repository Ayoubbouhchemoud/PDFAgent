using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using SharpDocument = PdfSharp.Pdf.PdfDocument;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace PDFAgent.PdfEngine.Redaction;

public sealed class PdfRedactionEngine : IRedactionEngine
{
    private readonly ILogger<PdfRedactionEngine> _logger;

    public PdfRedactionEngine(ILogger<PdfRedactionEngine> logger) => _logger = logger;

    public async Task<OperationResult> RedactTextAsync(
        string filePath, string outputPath,
        IReadOnlyList<RedactionTarget> targets,
        RedactionOptions options, CancellationToken ct = default)
    {
        return await Task.Run(() => ApplyRedactionRects(filePath, outputPath, targets, ct), ct);
    }

    public async Task<OperationResult> RedactPatternAsync(
        string filePath, string outputPath, string regexPattern,
        RedactionOptions options, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var targets = FindPatternTargets(filePath, regexPattern, ct);
                if (targets.Count == 0)
                {
                    File.Copy(filePath, outputPath, overwrite: true);
                    return OperationResult.Ok("No matches found — file copied unchanged");
                }
                return ApplyRedactionRects(filePath, outputPath, targets, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pattern redaction failed");
                return OperationResult.Fail($"Pattern redaction failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult> RedactPiiAsync(
        string filePath, string outputPath, PiiRedactionProfile profile,
        CancellationToken ct = default)
    {
        var patterns = BuildPiiPatterns(profile);
        if (patterns.Count == 0)
        {
            File.Copy(filePath, outputPath, overwrite: true);
            return OperationResult.Ok("No PII patterns selected — file copied unchanged");
        }
        var combined = string.Join("|", patterns.Select(p => $"(?:{p})"));
        return await RedactPatternAsync(filePath, outputPath, combined,
            new RedactionOptions { Author = "PDFAgent PII Redaction" }, ct);
    }

    public async Task<OperationResult<RedactionReport>> PreviewRedactionAsync(
        string filePath, IReadOnlyList<RedactionTarget> targets, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var matched = targets
                    .Where(t => !string.IsNullOrEmpty(t.TextMatch))
                    .Select(t => t.TextMatch!)
                    .ToList();
                var fi = new FileInfo(filePath);
                return OperationResult.Ok(new RedactionReport
                {
                    TotalRedactions = targets.Count,
                    PagesAffected = targets.Select(t => t.PageNumber).Distinct().Count(),
                    MatchedTexts = matched,
                    OriginalSize = fi.Length,
                    EstimatedOutputSize = fi.Length,
                });
            }
            catch (Exception ex)
            {
                return OperationResult.Fail<RedactionReport>($"Preview failed: {ex.Message}");
            }
        }, ct);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private List<RedactionTarget> FindPatternTargets(
        string filePath, string regexPattern, CancellationToken ct)
    {
        var targets = new List<RedactionTarget>();
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        using var pigDoc = PigDocument.Open(filePath);
        foreach (var page in pigDoc.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters);
            foreach (var word in words)
            {
                if (!regex.IsMatch(word.Text)) continue;
                var bb = word.BoundingBox;
                targets.Add(new RedactionTarget
                {
                    PageNumber = page.Number,
                    TextMatch = word.Text,
                    Bounds = new BoundingRect(bb.BottomLeft.X, bb.BottomLeft.Y, bb.Width, bb.Height),
                });
            }
        }
        _logger.LogInformation("Pattern redaction: {Count} matches found", targets.Count);
        return targets;
    }

    private OperationResult ApplyRedactionRects(
        string filePath, string outputPath,
        IReadOnlyList<RedactionTarget> targets, CancellationToken ct)
    {
        try
        {
            using var input = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            using var output = new SharpDocument();
            var byPage = targets.GroupBy(t => t.PageNumber - 1)
                                .ToDictionary(g => g.Key, g => g.ToList());

            for (var i = 0; i < input.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = output.AddPage(input.Pages[i]);

                if (!byPage.TryGetValue(i, out var pageTargets)) continue;

                double pageHeight = page.Height.Point;
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                foreach (var tgt in pageTargets)
                {
                    var b = tgt.Bounds;
                    // PdfPig uses bottom-left origin; PdfSharp XGraphics uses top-left
                    double x = b.X;
                    double y = pageHeight - b.Y - b.Height;
                    gfx.DrawRectangle(XBrushes.Black, x, y, b.Width, b.Height);
                }
            }
            output.Save(outputPath);
            _logger.LogInformation("Redacted {Count} regions → {Output}", targets.Count, outputPath);
            return OperationResult.Ok($"Redacted {targets.Count} region(s)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplyRedactionRects failed");
            return OperationResult.Fail($"Redaction failed: {ex.Message}");
        }
    }

    private static List<string> BuildPiiPatterns(PiiRedactionProfile profile)
    {
        var p = new List<string>();
        if (profile.RedactEmails) p.Add(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b");
        if (profile.RedactSsn) p.Add(@"\b\d{3}-\d{2}-\d{4}\b");
        if (profile.RedactCreditCards) p.Add(@"\b(?:\d[ \-]*?){13,16}\b");
        if (profile.RedactPhoneNumbers) p.Add(@"\b\d{3}[\-.]?\d{3}[\-.]?\d{4}\b");
        p.AddRange(profile.CustomPatterns);
        return p;
    }
}
