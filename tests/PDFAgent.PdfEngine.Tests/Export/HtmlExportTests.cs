using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PDFAgent.Core.Models;
using PDFAgent.PdfEngine.Export;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PDFAgent.PdfEngine.Tests.Export;

/// <summary>
/// Integration tests for PdfToHtmlConverter.
///
/// Acceptance criteria for the position-preserving layout:
///   1.  Converter succeeds and writes an output file.
///   2.  Output is a valid HTML5 document.
///   3.  Text appears as real HTML text — not a rasterised page screenshot.
///   4.  Body text lives inside absolutely-positioned &lt;span&gt; elements.
///   5.  Large-font text is represented with a larger CSS font-size.
///   6.  Two distinct text blocks produce two spans at different vertical positions.
///   7.  Bullet-prefixed text is preserved as real text.
///   8.  Page uses &lt;div class="pdf-page"&gt; — no old &lt;article&gt; / page-screenshot wrappers.
///   9.  Unsupported formats return a failure result without throwing.
///   10. Missing Python script returns a descriptive failure result.
///
/// Real-PDF tests (require the DTicket test file on disk):
///   11. DTicket text is NOT dumped into one giant &lt;table&gt;.
///   12. DTicket embedded logo is preserved as an &lt;img&gt; data-URI.
///   13. DTicket page container has the correct PDF page dimensions.
///
/// Tests 1–10 require Python 3 + pdfplumber on PATH.
/// Tests 11–13 also require the DTicket PDF in the repo's "test file" folder.
/// </summary>
public sealed class HtmlExportTests : IDisposable
{
    private readonly string             _dir;
    private readonly PdfToHtmlConverter _converter;

    public HtmlExportTests()
    {
        _dir       = Path.Combine(Path.GetTempPath(), $"html_export_{Guid.NewGuid():N}");
        _converter = new PdfToHtmlConverter(NullLogger<PdfToHtmlConverter>.Instance);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ── PDF fixture builders ──────────────────────────────────────────────────

    private string MakeTextPdf(string text = "Hello from PDFAgent export test.", int pages = 1)
    {
        var path = Path.Combine(_dir, $"text_{Guid.NewGuid():N}.pdf");
        using var doc = new PdfDocument();
        for (int i = 0; i < pages; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"{text} — page {i + 1}",
                new XFont("Arial", 12), XBrushes.Black, new XPoint(40, 60));
        }
        doc.Save(path);
        return path;
    }

    private string MakeHeadingPdf(string heading, int headingPt, string body, int bodyPt)
    {
        var path = Path.Combine(_dir, $"heading_{Guid.NewGuid():N}.pdf");
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawString(heading, new XFont("Arial", headingPt, XFontStyleEx.Bold),
            XBrushes.Black, new XPoint(40, 60));
        gfx.DrawString(body, new XFont("Arial", bodyPt),
            XBrushes.Black, new XPoint(40, 120));
        doc.Save(path);
        return path;
    }

    private string MakeTwoBlockPdf()
    {
        var path = Path.Combine(_dir, $"twoblocks_{Guid.NewGuid():N}.pdf");
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);
        gfx.DrawString("First block of text in the document.",    font, XBrushes.Black, new XPoint(40,  60));
        gfx.DrawString("Second block, separated by a large gap.", font, XBrushes.Black, new XPoint(40, 280));
        doc.Save(path);
        return path;
    }

    private string MakeBulletPdf()
    {
        var path = Path.Combine(_dir, $"bullet_{Guid.NewGuid():N}.pdf");
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);
        gfx.DrawString("- Alpha item", font, XBrushes.Black, new XPoint(40,  60));
        gfx.DrawString("- Beta item",  font, XBrushes.Black, new XPoint(40,  85));
        gfx.DrawString("- Gamma item", font, XBrushes.Black, new XPoint(40, 110));
        doc.Save(path);
        return path;
    }

    private async Task<string> ExportHtmlAndRead(string pdfPath)
    {
        var dest   = Path.Combine(_dir, $"out_{Guid.NewGuid():N}.html");
        var result = await _converter.ExportAsync(pdfPath, dest, ExportFormat.Html);
        result.IsSuccess.Should().BeTrue(result.Message);
        return File.ReadAllText(dest, Encoding.UTF8);
    }

    /// Walk up from the binary directory until the "test file" folder is found.
    private static string? FindDTicketPdf()
    {
        const string fileName = "DTicket_DTTDX3698212-2026-03.pdf";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "test file", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    // ── 1. Converter succeeds and produces a file ─────────────────────────────

    [Fact]
    public async Task ExportHtml_ReturnsSuccessAndWritesFile()
    {
        var pdf  = MakeTextPdf();
        var dest = Path.Combine(_dir, "out.html");

        var result = await _converter.ExportAsync(pdf, dest, ExportFormat.Html);

        result.IsSuccess.Should().BeTrue(result.Message);
        File.Exists(dest).Should().BeTrue("the output file must exist after a successful export");
        new FileInfo(dest).Length.Should().BeGreaterThan(200, "output must not be an empty file");
    }

    // ── 2. Output is a valid HTML5 document ───────────────────────────────────

    [Fact]
    public async Task ExportHtml_OutputIsHtml5()
    {
        var html = await ExportHtmlAndRead(MakeTextPdf());

        html.Should().StartWith("<!DOCTYPE html>", "must be a valid HTML5 document");
        html.Should().Contain("<html",  "must have an <html> root element");
        html.Should().Contain("<head>", "must have a <head> section");
        html.Should().Contain("<body>", "must have a <body> section");
    }

    // ── 3. Text is real and selectable — not a page screenshot ───────────────

    [Fact]
    public async Task ExportHtml_TextLayerIsRealText_NotScreenshot()
    {
        const string sourceText = "Selectable document content for the test.";
        var html = await ExportHtmlAndRead(MakeTextPdf(sourceText));

        html.Should().Contain(sourceText,
            "source text must appear verbatim as real HTML text");

        html.Should().NotContain("color:transparent",
            "the transparent-overlay rasterisation trick must not be used");
        html.Should().NotContain("class=\"tl\"",
            "old text-layer overlay markup must not appear");
        html.Should().NotContain("class=\"page\"",
            "bare page-screenshot wrapper class must not appear (pdf-page is allowed)");
    }

    // ── 4. Body text lives in absolutely-positioned <span> elements ───────────

    [Fact]
    public async Task ExportHtml_BodyTextIsInPositionedSpans()
    {
        const string text = "Paragraph content for test.";
        var html = await ExportHtmlAndRead(MakeTextPdf(text));

        html.Should().Contain("<span style=",
            "body text must be placed in styled <span> elements");
        html.Should().Contain("position:absolute",
            "spans must use position:absolute for faithful PDF-coordinate placement");
        html.Should().Contain(text,
            "the source text must appear verbatim inside a span");

        var spans = Regex.Matches(html, @"<span[^>]*>([\s\S]*?)</span>", RegexOptions.IgnoreCase);
        bool hasText = spans.Cast<Match>().Any(m =>
            Regex.Replace(m.Groups[1].Value, @"<[^>]+>", "").Trim().Length > 0);
        hasText.Should().BeTrue("at least one <span> must contain visible text");
    }

    // ── 5. Large-font text has a bigger CSS font-size than body text ──────────

    [Fact]
    public async Task ExportHtml_LargeFontHasBiggerCssFontSize()
    {
        var pdf  = MakeHeadingPdf("Main Report Title", headingPt: 24, "Body content.", bodyPt: 11);
        var html = await ExportHtmlAndRead(pdf);

        // Collect all font-size values from span styles
        var fontSizes = Regex.Matches(html, @"font-size:([\d.]+)px", RegexOptions.IgnoreCase)
            .Cast<Match>()
            .Select(m => double.Parse(m.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        fontSizes.Should().HaveCountGreaterThan(1,
            "a PDF with heading + body must produce spans with at least two different font sizes");

        var maxSize = fontSizes.Max();
        var minSize = fontSizes.Min();
        (maxSize / minSize).Should().BeGreaterThan(1.5,
            "the heading font must be noticeably larger than the body font (ratio > 1.5×)");
    }

    // ── 6. Two distinct text blocks appear at different vertical positions ─────

    [Fact]
    public async Task ExportHtml_TwoDistinctBlocks_AppearAtDifferentPositions()
    {
        var html = await ExportHtmlAndRead(MakeTwoBlockPdf());

        html.Should().Contain("First block of text",  "first block must be present");
        html.Should().Contain("Second block",          "second block must be present");

        // Both blocks must appear in positioned spans with distinct top values
        var topValues = Regex.Matches(html, @"top:([\d.]+)px", RegexOptions.IgnoreCase)
            .Cast<Match>()
            .Select(m => double.Parse(m.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();

        topValues.Should().HaveCountGreaterThanOrEqualTo(2,
            "two text blocks at different Y positions must produce spans with different top values");

        var gap = topValues.Max() - topValues.Min();
        gap.Should().BeGreaterThan(100,
            "blocks separated by ~220 pt in the PDF must differ by at least 100px in top");
    }

    // ── 7. Bullet-prefixed text is preserved as visible text ─────────────────

    [Fact]
    public async Task ExportHtml_BulletText_IsPreservedAsRealText()
    {
        var html = await ExportHtmlAndRead(MakeBulletPdf());

        html.Should().Contain("Alpha item",  "first bullet text must be present");
        html.Should().Contain("Beta item",   "second bullet text must be present");
        html.Should().Contain("Gamma item",  "third bullet text must be present");

        // Text must be in positioned spans, not a rasterised image
        html.Should().Contain("<span", "bullet text must appear in real HTML elements");
    }

    // ── 8. Page uses pdf-page div — no old article/page-screenshot wrappers ───

    [Fact]
    public async Task ExportHtml_UsesPageDiv_NoOldArticleOrScreenshotWrappers()
    {
        var html = await ExportHtmlAndRead(MakeTextPdf(pages: 2));

        html.Should().Contain("pdf-page",
            "each page must be wrapped in a <div class=\"pdf-page\"> container");
        html.Should().Contain("position:absolute",
            "the converter must use absolute positioning for content placement");

        // Old rasterised-page patterns must not appear
        html.Should().NotContain("<article>",       "old <article> wrapper must not appear");
        html.Should().NotContain("class=\"scanned\"", "scanned-image class must not appear");

        // Sanity-check: no class attribute value contains "scanned"
        foreach (Match m in Regex.Matches(html, @"class=""([^""]*)"""))
        {
            var cls = m.Groups[1].Value.ToLowerInvariant();
            cls.Should().NotContain("scanned", $"class '{cls}' must not reference 'scanned'");
        }
    }

    // ── 9. Unsupported format returns failure, does not throw ─────────────────

    [Fact]
    public async Task ExportHtml_UnsupportedFormat_ReturnsFailureNotException()
    {
        var pdf  = MakeTextPdf();
        var dest = Path.Combine(_dir, "bad.bin");

        var result = await _converter.ExportAsync(pdf, dest, (ExportFormat)999);

        result.IsSuccess.Should().BeFalse("unsupported format must return a failure result");
        result.Message.Should().NotBeNullOrWhiteSpace("failure must include a descriptive message");
    }

    // ── 10. Missing script returns a descriptive failure ──────────────────────

    [Fact]
    public async Task ExportHtml_MissingScript_ReturnsDescriptiveFailure()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "pdf_to_html.py");
        var backup     = scriptPath + ".bak_test";
        bool existed   = File.Exists(scriptPath);
        if (existed) File.Move(scriptPath, backup, overwrite: true);

        try
        {
            var result = await _converter.ExportAsync(
                MakeTextPdf(), Path.Combine(_dir, "out_noscript.html"), ExportFormat.Html);

            result.IsSuccess.Should().BeFalse("missing script must produce a failure result");
            result.Message.Should().NotBeNullOrWhiteSpace("failure must describe the problem");
        }
        finally
        {
            if (existed && File.Exists(backup))
                File.Move(backup, scriptPath, overwrite: true);
        }
    }

    // ── 11. DTicket: text is NOT all dumped into one giant table ─────────────

    [Fact]
    public async Task DTicket_TextIsInPositionedSpans_NotOneGiantTable()
    {
        var pdfPath = FindDTicketPdf();
        if (pdfPath is null)
            return;  // test file not present on this machine — skip

        var html = await ExportHtmlAndRead(pdfPath);

        // There must be exactly one <table> — only the real invoice table
        var tableCount = Regex.Matches(html, @"<table[^>]*>", RegexOptions.IgnoreCase).Count;
        tableCount.Should().Be(1,
            "the whole page must not be turned into a table; only the invoice table should be <table>");

        // Key content must appear as real text, not be swallowed by a table mis-parse
        html.Should().Contain("Abobestätigung",   "ticket title must be present as text");
        html.Should().Contain("Ayoub Bouhchemoud", "name must be present as text");
        html.Should().Contain("DTTDX3698212",      "subscription number must be present");
        html.Should().Contain("DTfTQRm7",          "ticket code must be present");
        html.Should().Contain("63,00",             "invoice amount must be present");

        // Body text must be in positioned <span> elements — not all crammed into a table
        var spanCount = Regex.Matches(html, @"<span[^>]*>", RegexOptions.IgnoreCase).Count;
        spanCount.Should().BeGreaterThan(5,
            "most of the page text must be placed as positioned <span> elements outside the table");
    }

    // ── 12. DTicket: embedded logo is an <img> element with a base64 data-URI ─

    [Fact]
    public async Task DTicket_EmbeddedLogoIsHtmlImageWithDataUri()
    {
        var pdfPath = FindDTicketPdf();
        if (pdfPath is null)
            return;

        var html = await ExportHtmlAndRead(pdfPath);

        html.Should().Contain("<img ",
            "embedded images must appear as <img> elements, not be discarded or rasterised");
        html.Should().Contain("data:image/",
            "image must be embedded as a base64 data-URI so it is self-contained");
        html.Should().Contain("position:absolute",
            "the <img> element must be absolutely positioned to match the original PDF placement");
    }

    // ── 13. DTicket: page container has the correct PDF dimensions ────────────

    [Fact]
    public async Task DTicket_PageContainerMatchesPdfPageDimensions()
    {
        var pdfPath = FindDTicketPdf();
        if (pdfPath is null)
            return;

        var html = await ExportHtmlAndRead(pdfPath);

        // A4 page is 595 × 842 pt; the container must reflect these values
        html.Should().MatchRegex(@"width:595(\.0)?px",
            "page container width must match the PDF's 595 pt A4 width");
        html.Should().Contain("pdf-page",
            "page must be wrapped in a div with class pdf-page");
    }
}
