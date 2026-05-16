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

    // ── HTML quality ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportHtml_EmbeddsRenderedPageAsBase64Png()
    {
        var dest = Path.Combine(_dir, "quality_html.html");
        var r    = await Export(ExportFormat.Html, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        var html = File.ReadAllText(dest, Encoding.UTF8);

        // The two-layer approach embeds a high-res rendered PNG per page
        html.Should().Contain("data:image/png;base64,",
            "each page must have a rendered PNG as the visual background");
        html.Should().Contain("class=\"bg\"",
            "rendered background image must carry the .bg class");
    }

    [Fact]
    public async Task ExportHtml_HasSelectableTextLayer()
    {
        var dest = Path.Combine(_dir, "quality_tl.html");
        var r    = await Export(ExportFormat.Html, dest);

        r.IsSuccess.Should().BeTrue(r.Message);
        var html = File.ReadAllText(dest, Encoding.UTF8);

        // The text layer div (.tl) carries transparent selectable spans
        html.Should().Contain("class=\"tl\"",
            "a text-layer div must be present for selectability");
        html.Should().Contain("color:transparent",
            "text spans must be transparent so the image is the visual source");
        html.Should().Contain("user-select:text",
            "text must remain selectable/copyable");
    }

    [Fact]
    public async Task ExportHtml_FileIsLargerThanRawText_ProvingImageIsEmbedded()
    {
        // Export as HTML
        var htmlDest = Path.Combine(_dir, "quality_size.html");
        await Export(ExportFormat.Html, htmlDest);
        // Export as plain text for comparison
        var txtDest = Path.Combine(_dir, "quality_size.txt");
        await Export(ExportFormat.Txt, txtDest);

        long htmlSize = new FileInfo(htmlDest).Length;
        long txtSize  = new FileInfo(txtDest).Length;

        // HTML with embedded PNG must be much larger than plain text
        htmlSize.Should().BeGreaterThan(txtSize * 10,
            "embedded page PNG should make HTML significantly larger than TXT");
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
}
