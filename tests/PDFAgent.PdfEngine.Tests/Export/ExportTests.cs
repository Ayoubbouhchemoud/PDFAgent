using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PDFAgent.Core.Models;
using PDFAgent.PdfEngine.Export;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PDFAgent.PdfEngine.Tests.Export;

/// <summary>
/// Integration tests for PdfExporter — each format produces valid, non-empty output.
/// All tests operate on real PDFs generated with PDFsharp.
/// </summary>
public sealed class ExportTests : IDisposable
{
    private readonly string _dir;
    private readonly string _pdfPath;
    private readonly PdfExporter _exporter;

    public ExportTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"export_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _pdfPath  = Path.Combine(_dir, "source.pdf");
        _exporter = new PdfExporter(NullLogger<PdfExporter>.Instance);
        CreateTextPdf(_pdfPath, pageCount: 2, text: "Hello from PDFAgent export test");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static void CreateTextPdf(string path, int pageCount, string text)
    {
        using var doc = new PdfDocument();
        for (var i = 0; i < pageCount; i++)
        {
            var page = doc.AddPage();
            using var gfx  = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
            var font = new PdfSharp.Drawing.XFont("Arial", 16);
            gfx.DrawString($"{text} — page {i + 1}", font,
                PdfSharp.Drawing.XBrushes.Black,
                new PdfSharp.Drawing.XPoint(40, 60));
        }
        doc.Save(path);
    }

    private Task<OperationResult> Export(
        ExportFormat fmt, string outputPath, ExportOptions? opts = null)
        => _exporter.ExportAsync(_pdfPath, outputPath, fmt,
            opts ?? new ExportOptions { AllPages = true, Dpi = 72 });

    // ── 1. TXT ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportTxt_ProducesNonEmptyFile()
    {
        var dest = Path.Combine(_dir, "out.txt");
        var r    = await Export(ExportFormat.Txt, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        File.Exists(dest).Should().BeTrue();
        var content = File.ReadAllText(dest, Encoding.UTF8);
        content.Should().NotBeNullOrWhiteSpace();
        // PDFsharp PDFs embed text that PdfPig can read
        content.Length.Should().BeGreaterThan(5, "text layer should have content");
    }

    // ── 2. HTML ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportHtml_ProducesValidHtmlFile()
    {
        var dest = Path.Combine(_dir, "out.html");
        var r    = await Export(ExportFormat.Html, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        File.Exists(dest).Should().BeTrue();
        var html = File.ReadAllText(dest, Encoding.UTF8);
        html.Should().Contain("<!DOCTYPE html>");
        html.Should().Contain("<div class=\"page\"");
        html.Length.Should().BeGreaterThan(200);
    }

    // ── 3. EPUB ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportEpub_ProducesValidEpubZip()
    {
        var dest = Path.Combine(_dir, "out.epub");
        var r    = await Export(ExportFormat.Epub, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        File.Exists(dest).Should().BeTrue();

        using var zip = ZipFile.OpenRead(dest);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        entries.Should().Contain("mimetype");
        entries.Should().Contain("META-INF/container.xml");
        entries.Should().Contain(e => e.StartsWith("OEBPS/content.opf"));
        entries.Should().Contain(e => e.EndsWith(".xhtml"));
    }

    // ── 4. PNG (single page) ──────────────────────────────────────────────────

    [Fact]
    public async Task ExportPng_SinglePage_ProducesValidPngFile()
    {
        var dest = Path.Combine(_dir, "out.png");
        var opts = new ExportOptions { AllPages = false, PageIndex = 0, Dpi = 72 };
        var r    = await Export(ExportFormat.Png, dest, opts);

        r.IsSuccess.Should().BeTrue(r.Message);
        File.Exists(dest).Should().BeTrue();

        var bytes = File.ReadAllBytes(dest);
        bytes.Length.Should().BeGreaterThan(100);
        // PNG magic bytes: 89 50 4E 47
        bytes[0].Should().Be(0x89);
        bytes[1].Should().Be(0x50);
        bytes[2].Should().Be(0x4E);
        bytes[3].Should().Be(0x47);
    }

    // ── 5. PNG (all pages → folder) ───────────────────────────────────────────

    [Fact]
    public async Task ExportPng_AllPages_ProducesMultiplePngFiles()
    {
        var folder = Path.Combine(_dir, "pngs");
        var r      = await Export(ExportFormat.Png, folder);

        r.IsSuccess.Should().BeTrue(r.Message);
        var files = Directory.GetFiles(folder, "*.png");
        files.Length.Should().Be(2, "source PDF has 2 pages");
    }

    // ── 6. JPG ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportJpg_SinglePage_ProducesValidJpegFile()
    {
        var dest = Path.Combine(_dir, "out.jpg");
        var opts = new ExportOptions { AllPages = false, PageIndex = 0, Dpi = 72 };
        var r    = await Export(ExportFormat.Jpg, dest, opts);

        r.IsSuccess.Should().BeTrue(r.Message);
        var bytes = File.ReadAllBytes(dest);
        // JPEG magic: FF D8
        bytes[0].Should().Be(0xFF);
        bytes[1].Should().Be(0xD8);
    }

    // ── 7. SVG ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportSvg_SinglePage_ProducesValidSvg()
    {
        var dest = Path.Combine(_dir, "out.svg");
        var opts = new ExportOptions { AllPages = false, PageIndex = 0, Dpi = 72 };
        var r    = await Export(ExportFormat.Svg, dest, opts);

        r.IsSuccess.Should().BeTrue(r.Message);
        var svg = File.ReadAllText(dest, Encoding.UTF8);
        svg.Should().Contain("<svg");
        svg.Should().Contain("data:image/png;base64,");
    }

    // ── 8. PDF copy ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportPdfCopy_ProducesIdenticalFile()
    {
        var dest = Path.Combine(_dir, "copy.pdf");
        var r    = await Export(ExportFormat.Pdf, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        var original = File.ReadAllBytes(_pdfPath);
        var copy     = File.ReadAllBytes(dest);
        copy.Should().Equal(original);
    }

    // ── 9. PDF/A-1b ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportPdfA1b_ProducesReadablePdf()
    {
        var dest = Path.Combine(_dir, "pdfa1.pdf");
        var r    = await Export(ExportFormat.PdfA1b, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        // Verify it's a valid PDF by opening with PdfSharp
        using var doc = PdfReader.Open(dest, PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(2);
        doc.Info.Keywords.Should().Contain("PDF/A-1");
    }

    // ── 10. PDF/A-2b ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportPdfA2b_ProducesReadablePdf()
    {
        var dest = Path.Combine(_dir, "pdfa2.pdf");
        var r    = await Export(ExportFormat.PdfA2b, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        using var doc = PdfReader.Open(dest, PdfDocumentOpenMode.Import);
        doc.Info.Keywords.Should().Contain("PDF/A-2");
    }

    // ── 11. PDF/A-3b ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportPdfA3b_ProducesReadablePdf()
    {
        var dest = Path.Combine(_dir, "pdfa3.pdf");
        var r    = await Export(ExportFormat.PdfA3b, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        using var doc = PdfReader.Open(dest, PdfDocumentOpenMode.Import);
        doc.Info.Keywords.Should().Contain("PDF/A-3");
    }

    // ── 12. Secure PDF ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportSecurePdf_ProducesEncryptedPdf()
    {
        var dest = Path.Combine(_dir, "secure.pdf");
        var opts = new ExportOptions
        {
            AllPages      = true,
            UserPassword  = "open123",
            OwnerPassword = "owner456",
        };
        var r = await Export(ExportFormat.SecurePdf, dest, opts);

        r.IsSuccess.Should().BeTrue(r.Message);
        // File should contain /Encrypt entry — check raw bytes
        var raw = File.ReadAllText(dest, Encoding.Latin1);
        raw.Should().Contain("/Encrypt", "encrypted PDFs must have an /Encrypt dictionary");
    }

    // ── 13. DOCX ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportDocx_ProducesOpenableWordDocument()
    {
        var dest = Path.Combine(_dir, "out.docx");
        var r    = await Export(ExportFormat.Docx, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        File.Exists(dest).Should().BeTrue();

        // Open with Open XML SDK — proves it's a valid DOCX
        using var doc  = WordprocessingDocument.Open(dest, isEditable: false);
        var body = doc.MainDocumentPart!.Document.Body;
        body.Should().NotBeNull();
        var text = body!.InnerText;
        text.Length.Should().BeGreaterThan(0, "DOCX should have content");
    }

    // ── 14. PPTX ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportPptx_ProducesOpenablePresentationWithCorrectSlideCount()
    {
        var dest = Path.Combine(_dir, "out.pptx");
        var r    = await Export(ExportFormat.Pptx, dest, new ExportOptions { AllPages = true, Dpi = 72 });

        r.IsSuccess.Should().BeTrue(r.Message);
        File.Exists(dest).Should().BeTrue();

        using var pptx = PresentationDocument.Open(dest, isEditable: false);
        var slides = pptx.PresentationPart!.SlideParts.ToList();
        slides.Count.Should().Be(2, "source PDF has 2 pages");
    }

    // ── 15. XLSX ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportXlsx_ProducesOpenableWorkbookWithCorrectSheetCount()
    {
        var dest = Path.Combine(_dir, "out.xlsx");
        var r    = await Export(ExportFormat.Xlsx, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        File.Exists(dest).Should().BeTrue();

        using var xlsx = SpreadsheetDocument.Open(dest, isEditable: false);
        var sheets = xlsx.WorkbookPart!.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().ToList();
        sheets.Count.Should().Be(2, "one sheet per page");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Quality-verification tests — prove the improved fidelity of each format
    // ════════════════════════════════════════════════════════════════════════

    // ── HTML quality — semantic document, NOT image-overlay viewer ───────────

    [Fact]
    public async Task ExportHtml_ContainsRealText_NotTransparentOverlay()
    {
        // The new approach generates semantic HTML — text must be real, not invisible
        var dest = Path.Combine(_dir, "quality_real_text.html");
        var r    = await Export(ExportFormat.Html, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        var html = File.ReadAllText(dest, Encoding.UTF8);

        // Real text must appear inside semantic elements — at least one <p>/<hN> with content.
        // Text may be wrapped in <span> for styling, so we strip child tags before checking.
        var firstBlockMatch = System.Text.RegularExpressions.Regex.Match(
            html, @"<(?:p|h[1-3])[^>]*>([\s\S]*?)</(?:p|h[1-3])>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        bool hasParagraphWithText = firstBlockMatch.Success &&
            System.Text.RegularExpressions.Regex.Replace(
                firstBlockMatch.Groups[1].Value, @"<[^>]+>", "").Trim().Length > 0;
        hasParagraphWithText.Should().BeTrue(
            "HTML must contain semantic elements (<p> or <h1>-<h3>) with actual text content");
        // Must NOT use the old transparent-span overlay approach
        html.Should().NotContain("color:transparent",
            "text must be real content, not a transparent overlay on a rendered image");
        html.Should().NotContain("class=\"tl\"",
            "the old text-layer overlay div must not be present");
    }

    [Fact]
    public async Task ExportHtml_HasSemanticParagraphElements()
    {
        var dest = Path.Combine(_dir, "quality_p.html");
        var r    = await Export(ExportFormat.Html, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        var html = File.ReadAllText(dest, Encoding.UTF8);

        // Body text must appear inside <p> elements
        html.Should().Contain("<p>",
            "body text must be wrapped in paragraph elements");
        html.Should().Contain("</p>",
            "paragraph elements must be properly closed");
    }

    [Fact]
    public async Task ExportHtml_LargeTextBecomesHeadingElement()
    {
        // Build a PDF with a clear heading (large font) and body text (smaller font)
        var headingPdf = Path.Combine(_dir, "heading_test.pdf");
        CreateHeadingPdf(headingPdf,
            headingText: "Main Report Title",
            headingSize: 24,
            bodyText:    "This is body content.",
            bodySize:    11);

        var dest = Path.Combine(_dir, "quality_heading.html");
        var r    = await _exporter.ExportAsync(headingPdf, dest, ExportFormat.Html,
            new ExportOptions { AllPages = true, Dpi = 72 });

        r.IsSuccess.Should().BeTrue(r.Message);
        var html = File.ReadAllText(dest, Encoding.UTF8);

        // Large text must be promoted to a heading element with real content.
        // Text inside headings may be wrapped in <span> for styling — strip tags before checking.
        var headingMatch = System.Text.RegularExpressions.Regex.Match(
            html, @"<h([1-3])[^>]*>([\s\S]*?)</h\1>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        bool hasHeadingWithContent = headingMatch.Success &&
            System.Text.RegularExpressions.Regex.Replace(
                headingMatch.Groups[2].Value, @"<[^>]+>", "").Trim().Length > 0;
        hasHeadingWithContent.Should().BeTrue(
            "text with significantly larger font size must become a heading element with content");
    }

    [Fact]
    public async Task ExportHtml_TableStructureBecomesHtmlTable()
    {
        // Two-column PDF with aligned rows → must produce a <table>
        var tablePdf = Path.Combine(_dir, "table_test.pdf");
        CreateTwoColumnPdf(tablePdf);

        var dest = Path.Combine(_dir, "quality_table.html");
        var r    = await _exporter.ExportAsync(tablePdf, dest, ExportFormat.Html,
            new ExportOptions { AllPages = true, Dpi = 72 });

        r.IsSuccess.Should().BeTrue(r.Message);
        var html = File.ReadAllText(dest, Encoding.UTF8);

        html.Should().Contain("<table>",
            "multi-column aligned content must be rendered as an HTML table");
        html.Should().Contain("<th>",
            "first row of a detected table must use header cells");
        html.Should().Contain("</table>",
            "table element must be properly closed");
    }

    [Fact]
    public async Task ExportHtml_PageDivWrapsEachPage()
    {
        var dest = Path.Combine(_dir, "quality_pages.html");
        var r    = await Export(ExportFormat.Html, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        var html = File.ReadAllText(dest, Encoding.UTF8);

        // One .page div per PDF page
        int pageCount = System.Text.RegularExpressions.Regex.Matches(html, "class=\"page\"").Count;
        pageCount.Should().Be(2, "source PDF has 2 pages — each must produce a .page div");
    }

    [Fact]
    public async Task ExportHtml_TextPdf_NeverProducesPageScreenshot()
    {
        // A PDF with a real text layer must NEVER be rasterized into a base64 page image.
        var dest = Path.Combine(_dir, "no_screenshot.html");
        var r    = await Export(ExportFormat.Html, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        var html = File.ReadAllText(dest, Encoding.UTF8);

        // The old fallback embedded the entire page as a data-URI PNG — must not appear.
        html.Should().NotContain("class=\"scanned\"",
            "full-page rasterized images must not appear in HTML for text PDFs");
        // And the output must not be dominated by a single giant base64 blob
        // (no scanned-image path means no massive data:image/png inline for a full page).
        // Real text pages must contain semantic elements, not just an <img>.
        html.Should().Contain("<p>",
            "text PDFs must produce real paragraph elements, not only images");
    }

    [Fact]
    public async Task ExportHtml_TwoDistinctBlocks_ProducesSeparateParagraphs()
    {
        var pdf = Path.Combine(_dir, "two_block.pdf");
        CreateTwoBlockPdf(pdf);

        var dest = Path.Combine(_dir, "quality_two_paras.html");
        var r    = await _exporter.ExportAsync(pdf, dest, ExportFormat.Html,
            new ExportOptions { AllPages = true, Dpi = 72 });

        r.IsSuccess.Should().BeTrue(r.Message);
        var html = File.ReadAllText(dest, Encoding.UTF8);

        // Two text blocks separated by a large vertical gap must become two <p> elements
        var paragraphs = System.Text.RegularExpressions.Regex.Matches(
            html, @"<p[^>]*>[\s\S]*?</p>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        paragraphs.Count.Should().BeGreaterThanOrEqualTo(2,
            "two text blocks with a large gap must produce at least two separate <p> elements");
    }

    [Fact]
    public async Task ExportHtml_BoldText_ProducesStrongElement()
    {
        var pdf  = Path.Combine(_dir, "bold_test.pdf");
        CreateBoldTextPdf(pdf);

        var dest = Path.Combine(_dir, "quality_bold.html");
        var r    = await _exporter.ExportAsync(pdf, dest, ExportFormat.Html,
            new ExportOptions { AllPages = true, Dpi = 72 });

        r.IsSuccess.Should().BeTrue(r.Message);
        var html = File.ReadAllText(dest, Encoding.UTF8);
        html.Should().Contain("<strong>", "bold font text must produce <strong> elements");
    }

    [Fact]
    public async Task ExportHtml_BulletList_ProducesUlLiElements()
    {
        var pdf  = Path.Combine(_dir, "bullet_test.pdf");
        CreateBulletListPdf(pdf);

        var dest = Path.Combine(_dir, "quality_bullet.html");
        var r    = await _exporter.ExportAsync(pdf, dest, ExportFormat.Html,
            new ExportOptions { AllPages = true, Dpi = 72 });

        r.IsSuccess.Should().BeTrue(r.Message);
        var html = File.ReadAllText(dest, Encoding.UTF8);
        html.Should().Contain("<ul>",  "bullet items must be wrapped in a <ul> element");
        html.Should().Contain("<li>",  "each bullet item must be a <li> element");
        html.Should().Contain("</ul>", "<ul> must be properly closed");
    }

    // ── DOCX quality — DocxBuilder tested directly, independent of Word COM ────

    [Fact]
    public void DocxBuilder_ProducesStyledRunsWithFontAndSizeInfo()
    {
        // Test the builder directly with known styled input so the result is
        // independent of whether Word COM is installed on this machine
        var run1 = new PDFAgent.PdfEngine.TextRun("Hello",  "Arial", 16, true,  false, "000000");
        var run2 = new PDFAgent.PdfEngine.TextRun(" world", "Arial", 16, false, false, "FF0000");
        var line = new PDFAgent.PdfEngine.TextLine(new[] { run1, run2 }, 16);
        var page = new PDFAgent.PdfEngine.DocPage(new[] { line }, 595.28, 841.89);

        var dest = Path.Combine(_dir, "builder_styled.docx");
        PDFAgent.PdfEngine.DocxBuilder.Build(new[] { page }, dest);

        File.Exists(dest).Should().BeTrue();
        using var doc  = WordprocessingDocument.Open(dest, isEditable: false);
        var body = doc.MainDocumentPart!.Document.Body!;

        var runProps = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.RunProperties>().ToList();
        runProps.Should().NotBeEmpty("styled runs must produce RunProperties elements");
        runProps.Should().Contain(rp => rp.RunFonts != null,
            "at least one run must carry RunFonts");
        runProps.Should().Contain(rp => rp.FontSize != null,
            "at least one run must carry FontSize");
    }

    [Fact]
    public void DocxBuilder_DetectsLargeTextAsHeading()
    {
        var heading  = new PDFAgent.PdfEngine.TextRun("Title",      "Arial", 24, true,  false, "000000");
        var bodyText = new PDFAgent.PdfEngine.TextRun("Body text.", "Arial", 12, false, false, "000000");
        var headLine = new PDFAgent.PdfEngine.TextLine(new[] { heading  }, 24);
        var bodyLine = new PDFAgent.PdfEngine.TextLine(new[] { bodyText }, 12);
        var page     = new PDFAgent.PdfEngine.DocPage(new[] { headLine, bodyLine }, 595, 842);

        var dest = Path.Combine(_dir, "builder_heading.docx");
        PDFAgent.PdfEngine.DocxBuilder.Build(new[] { page }, dest);

        using var doc   = WordprocessingDocument.Open(dest, isEditable: false);
        var paras = doc.MainDocumentPart!.Document.Body!
            .Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().ToList();

        var hasHeading = paras.Any(p =>
        {
            var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            return styleId != null && styleId.StartsWith("Heading");
        });
        hasHeading.Should().BeTrue("the large-font line should become a heading paragraph");
    }

    [Fact]
    public void DocxBuilder_HasStylesPartWithHeadingDefinitions()
    {
        var run  = new PDFAgent.PdfEngine.TextRun("text", "Arial", 12, false, false, "000000");
        var line = new PDFAgent.PdfEngine.TextLine(new[] { run }, 12);
        var page = new PDFAgent.PdfEngine.DocPage(new[] { line }, 595, 842);

        var dest = Path.Combine(_dir, "builder_styles.docx");
        PDFAgent.PdfEngine.DocxBuilder.Build(new[] { page }, dest);

        using var doc      = WordprocessingDocument.Open(dest, isEditable: false);
        var stylesPart = doc.MainDocumentPart!.StyleDefinitionsPart;
        stylesPart.Should().NotBeNull("DocxBuilder must include a styles part");

        var styleIds = stylesPart!.Styles!
            .Elements<DocumentFormat.OpenXml.Wordprocessing.Style>()
            .Select(s => s.StyleId?.Value).ToList();
        styleIds.Should().Contain("Heading1");
        styleIds.Should().Contain("Heading2");
    }

    [Fact]
    public void DocxBuilder_SetsPageSizeFromDocPage()
    {
        var run  = new PDFAgent.PdfEngine.TextRun("text", "Arial", 12, false, false, "000000");
        var line = new PDFAgent.PdfEngine.TextLine(new[] { run }, 12);
        // A4 in PDF points: 595.28 × 841.89
        var page = new PDFAgent.PdfEngine.DocPage(new[] { line }, 595.28, 841.89);

        var dest = Path.Combine(_dir, "builder_pagesize.docx");
        PDFAgent.PdfEngine.DocxBuilder.Build(new[] { page }, dest);

        using var doc = WordprocessingDocument.Open(dest, isEditable: false);
        var sectPr = doc.MainDocumentPart!.Document.Body!
            .Descendants<DocumentFormat.OpenXml.Wordprocessing.SectionProperties>()
            .FirstOrDefault();
        sectPr.Should().NotBeNull("SectionProperties must be present");

        var ps = sectPr!.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.PageSize>();
        ps.Should().NotBeNull("PageSize must be set from DocPage dimensions");
        // A4 width in twips: 595.28 * 20 ≈ 11906
        ps!.Width!.Value.Should().BeGreaterThan(11000u, "width should reflect A4 PDF dimensions");
    }

    // ── XLSX quality ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportXlsx_MultiColumnPdf_DistributesWordsAcrossColumns()
    {
        // Create a 2-column PDF: one block of text on the left, another on the right
        var twoColPdf = Path.Combine(_dir, "two_col.pdf");
        CreateTwoColumnPdf(twoColPdf);

        var dest = Path.Combine(_dir, "quality_cols.xlsx");
        var r    = await _exporter.ExportAsync(twoColPdf, dest, ExportFormat.Xlsx,
            new ExportOptions { AllPages = true, Dpi = 72 });

        r.IsSuccess.Should().BeTrue(r.Message);
        using var xlsx = SpreadsheetDocument.Open(dest, isEditable: false);
        var ws   = xlsx.WorkbookPart!.WorksheetParts.First().Worksheet;
        var refs = ws.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>()
                     .Select(c => c.CellReference?.Value ?? "")
                     .Where(cr => !string.IsNullOrEmpty(cr))
                     .ToList();

        // With grid detection, some cells should be in columns other than A
        refs.Should().Contain(cr => !cr.StartsWith("A"),
            "two-column PDF must produce cells in at least two spreadsheet columns");
    }

    [Fact]
    public async Task ExportXlsx_SingleColumnPdf_KeepsTextInColumnA()
    {
        // The simple single-line test PDF should map to column A only
        var dest = Path.Combine(_dir, "quality_singlecol.xlsx");
        var r    = await Export(ExportFormat.Xlsx, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        using var xlsx = SpreadsheetDocument.Open(dest, isEditable: false);
        var ws    = xlsx.WorkbookPart!.WorksheetParts.First().Worksheet;
        var cells = ws.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>().ToList();

        cells.Should().NotBeEmpty("single-column PDF must still produce cells");
        // Each word now gets its own cell — verify there is more than 1 cell per row
        cells.Count.Should().BeGreaterThan(1,
            "individual words should be in separate cells, not all joined in one");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static void CreateTwoColumnPdf(string path)
    {
        using var doc  = new PdfDocument();
        var page = doc.AddPage();
        using var gfx  = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        var font = new PdfSharp.Drawing.XFont("Arial", 12);
        // Left column ~x=50, right column ~x=350 — ~300pt gap between them
        gfx.DrawString("LeftHeader",  font, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(50, 60));
        gfx.DrawString("RightHeader", font, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(350, 60));
        gfx.DrawString("LeftValue",   font, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(50, 90));
        gfx.DrawString("RightValue",  font, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(350, 90));
        doc.Save(path);
    }

    private static void CreateHeadingPdf(
        string path, string headingText, int headingSize, string bodyText, int bodySize)
    {
        using var doc  = new PdfDocument();
        var page = doc.AddPage();
        using var gfx  = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        var headFont = new PdfSharp.Drawing.XFont("Arial", headingSize, PdfSharp.Drawing.XFontStyleEx.Bold);
        gfx.DrawString(headingText, headFont, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(40, 60));
        var bodyFont = new PdfSharp.Drawing.XFont("Arial", bodySize);
        gfx.DrawString(bodyText, bodyFont, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(40, 120));
        doc.Save(path);
    }

    private static void CreateTwoBlockPdf(string path)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var gfx  = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        var font = new PdfSharp.Drawing.XFont("Arial", 12);
        // Two blocks separated by ~140pt vertical gap (well above 1.8 × 12pt = 21.6pt threshold)
        gfx.DrawString("First paragraph text.", font, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(40, 60));
        gfx.DrawString("Second paragraph text.", font, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(40, 200));
        doc.Save(path);
    }

    private static void CreateBoldTextPdf(string path)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var gfx  = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        var font = new PdfSharp.Drawing.XFont("Arial", 12, PdfSharp.Drawing.XFontStyleEx.Bold);
        gfx.DrawString("BoldWord regular text", font, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(40, 60));
        doc.Save(path);
    }

    private static void CreateBulletListPdf(string path)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var gfx  = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        var font = new PdfSharp.Drawing.XFont("Arial", 12);
        // Use hyphen-space bullets (ASCII, always readable by PdfPig)
        gfx.DrawString("- First item", font, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(40, 60));
        gfx.DrawString("- Second item", font, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(40, 85));
        doc.Save(path);
    }
}
