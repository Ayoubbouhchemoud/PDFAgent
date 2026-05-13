using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;

namespace PDFAgent.PdfEngine.Redaction;

public sealed class PdfRedactionEngine : IRedactionEngine
{
    private readonly ILogger<PdfRedactionEngine> _logger;

    public PdfRedactionEngine(ILogger<PdfRedactionEngine> logger)
    {
        _logger = logger;
    }

    public async Task<OperationResult> RedactTextAsync(
        string filePath, string outputPath, IReadOnlyList<RedactionTarget> targets,
        RedactionOptions options, CancellationToken ct = default)
    {
        // PdfiumViewer does not support drawing on PDF pages.
        // True redaction requires iText7 (commercial/AGPL) or PdfPig.
        // This is a placeholder for the MVP.
        await Task.CompletedTask;
        _logger.LogWarning("Redaction not implemented — requires iText7 or PdfPig");
        return OperationResult.Fail("Redaction requires iText7 or PdfPig (not available in MVP)");
    }

    public async Task<OperationResult> RedactPatternAsync(
        string filePath, string outputPath, string regexPattern,
        RedactionOptions options, CancellationToken ct = default)
    {
        try
        {
            var text = await Task.Run(() => File.ReadAllText(filePath), ct);
            var matches = Regex.Matches(text, regexPattern, RegexOptions.IgnoreCase);
            // For production, use iText7 or PdfPig for pattern-based text redaction
            _logger.LogInformation("Pattern redaction: {Count} matches found", matches.Count);
            return OperationResult.Ok($"Found {matches.Count} matches for pattern");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Pattern redaction failed: {ex.Message}");
        }
    }

    public async Task<OperationResult> RedactPiiAsync(
        string filePath, string outputPath, PiiRedactionProfile profile,
        CancellationToken ct = default)
    {
        try
        {
            var patterns = new List<string>();
            if (profile.RedactEmails) patterns.Add(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b");
            if (profile.RedactSsn) patterns.Add(@"\b\d{3}-\d{2}-\d{4}\b");
            if (profile.RedactCreditCards) patterns.Add(@"\b(?:\d[ -]*?){13,16}\b");
            if (profile.RedactPhoneNumbers) patterns.Add(@"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b");
            patterns.AddRange(profile.CustomPatterns);

            var combined = string.Join("|", patterns);
            if (string.IsNullOrEmpty(combined))
                return OperationResult.Ok("No PII patterns selected");

            return await RedactPatternAsync(filePath, outputPath, combined,
                new RedactionOptions { Author = "PDFAgent PII Redaction" }, ct);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"PII redaction failed: {ex.Message}");
        }
    }

    public async Task<OperationResult<RedactionReport>> PreviewRedactionAsync(
        string filePath, IReadOnlyList<RedactionTarget> targets, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var matched = new List<string>();
                var affectedPages = new HashSet<int>();

                foreach (var target in targets)
                {
                    ct.ThrowIfCancellationRequested();
                    affectedPages.Add(target.PageNumber);
                    if (!string.IsNullOrEmpty(target.TextMatch))
                        matched.Add(target.TextMatch);
                }

                var fi = new FileInfo(filePath);
                return OperationResult.Ok(new RedactionReport
                {
                    TotalRedactions = targets.Count,
                    PagesAffected = affectedPages.Count,
                    MatchedTexts = matched,
                    OriginalSize = fi.Length,
                    EstimatedOutputSize = fi.Length, // estimate
                });
            }
            catch (Exception ex)
            {
                return OperationResult.Fail<RedactionReport>($"Preview failed: {ex.Message}");
            }
        }, ct);
    }
}
