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
/// Acceptance criteria verified by this suite:
///   1. Output is real HTML5, not a rasterized page image.
///   2. Text appears inside &lt;p&gt; elements as selectable text.
///   3. Large-font text becomes &lt;h1&gt;/&lt;h2&gt;/&lt;h3&gt;.
///   4. Two separated text blocks become two &lt;p&gt; elements.
///   5. Bullet items produce &lt;ul&gt;&lt;li&gt; markup.
///   6. One &lt;article&gt; wraps the document — no &lt;div class="page"&gt; wrappers.
///   7. Unsupported formats return a failure result without throwing.
///   8. Missing Python script returns a descriptive failure result.
///
/// Tests 1–6 require Python 3 + pdfplumber on PATH.
/// If Python is not installed the converter returns a failure result whose
/// message describes the missing dependency — tests will fail with that message.
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
        // Two text blocks separated by ~220pt — well above the 1.8× line-height threshold.
        gfx.DrawString("First block of text in the document.",     font, XBrushes.Black, new XPoint(40,  60));
        gfx.DrawString("Second block, separated by a large gap.",  font, XBrushes.Black, new XPoint(40, 280));
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
        // Hyphen-space bullets are reliably read by pdfplumber.
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
            "the transparent-overlay rasterization trick must not be used");
        html.Should().NotContain("class=\"tl\"",
            "old text-layer overlay markup must not appear");
        html.Should().NotContain("class=\"page\"",
            "page-screenshot wrapper divs must not appear");
    }

    // ── 4. Body text appears inside <p> elements ──────────────────────────────

    [Fact]
    public async Task ExportHtml_BodyTextIsInsideParagraphElements()
    {
        var html = await ExportHtmlAndRead(MakeTextPdf("Paragraph content for test."));

        html.Should().Contain("<p>",  "body text must be wrapped in <p> elements");
        html.Should().Contain("</p>", "<p> elements must be properly closed");

        var paras = Regex.Matches(html, @"<p[^>]*>([\s\S]*?)</p>", RegexOptions.IgnoreCase);
        paras.Count.Should().BeGreaterThan(0, "at least one <p> element must exist");

        bool hasText = paras.Cast<Match>().Any(m =>
            Regex.Replace(m.Groups[1].Value, @"<[^>]+>", "").Trim().Length > 0);
        hasText.Should().BeTrue("at least one <p> must contain visible text, not be empty");
    }

    // ── 5. Large font becomes a heading element ───────────────────────────────

    [Fact]
    public async Task ExportHtml_LargeFontBecomesHeadingElement()
    {
        var pdf  = MakeHeadingPdf("Main Report Title", headingPt: 24, "Body content.", bodyPt: 11);
        var html = await ExportHtmlAndRead(pdf);

        var headings = Regex.Matches(html, @"<h([1-3])[^>]*>([\s\S]*?)</h\1>", RegexOptions.IgnoreCase);
        headings.Count.Should().BeGreaterThan(0,
            "text with a significantly larger font must produce an h1/h2/h3 element");

        bool hasText = headings.Cast<Match>().Any(m =>
            Regex.Replace(m.Groups[2].Value, @"<[^>]+>", "").Trim().Length > 0);
        hasText.Should().BeTrue("the heading element must contain visible text");
    }

    // ── 6. Two separated text blocks become two <p> elements ─────────────────

    [Fact]
    public async Task ExportHtml_TwoDistinctBlocks_ProduceTwoParagraphs()
    {
        var html = await ExportHtmlAndRead(MakeTwoBlockPdf());

        var paras = Regex.Matches(html, @"<p[^>]*>[\s\S]*?</p>", RegexOptions.IgnoreCase);
        paras.Count.Should().BeGreaterThanOrEqualTo(2,
            "two text blocks with a large vertical gap must produce at least two <p> elements");
    }

    // ── 7. Bullet items produce ul/li markup ──────────────────────────────────

    [Fact]
    public async Task ExportHtml_BulletItems_ProduceUlLiElements()
    {
        var html = await ExportHtmlAndRead(MakeBulletPdf());

        html.Should().Contain("<ul>",  "bullet items must be wrapped in a <ul> element");
        html.Should().Contain("<li>",  "each bullet item must be a <li> element");
        html.Should().Contain("</ul>", "<ul> must be properly closed");
    }

    // ── 8. Single article wrapper, no page-div wrappers ──────────────────────

    [Fact]
    public async Task ExportHtml_UsesOneArticle_NoPageWrapperDivs()
    {
        var html = await ExportHtmlAndRead(MakeTextPdf(pages: 2));

        html.Should().Contain("<article>",  "document must be wrapped in a single <article>");
        html.Should().Contain("</article>", "<article> must be properly closed");
        Regex.Matches(html, "<article>", RegexOptions.IgnoreCase).Count
             .Should().Be(1, "exactly one <article> element must exist");

        html.Should().NotContain("class=\"page\"",    "page-wrapper divs must not appear");
        html.Should().NotContain("class=\"scanned\"", "scanned-image classes must not appear");

        foreach (Match m in Regex.Matches(html, @"class=""([^""]*)"""))
        {
            var cls = m.Groups[1].Value.ToLowerInvariant();
            cls.Should().NotContain("page",    $"class '{cls}' must not reference 'page'");
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
}
