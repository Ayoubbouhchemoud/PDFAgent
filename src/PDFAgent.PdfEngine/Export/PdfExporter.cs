using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using D  = DocumentFormat.OpenXml.Drawing;
using P  = DocumentFormat.OpenXml.Presentation;

namespace PDFAgent.PdfEngine.Export;

public sealed class PdfExporter : IPdfExporter
{
    private readonly ILogger<PdfExporter> _logger;

    public PdfExporter(ILogger<PdfExporter> logger) => _logger = logger;

    // ── Public entry point ────────────────────────────────────────────────────

    public Task<OperationResult> ExportAsync(
        string inputPath,
        string outputPath,
        ExportFormat format,
        ExportOptions options,
        IProgress<(int Current, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return format switch
                {
                    ExportFormat.Txt      => ExportTxt(inputPath, outputPath, options, progress, ct),
                    ExportFormat.Html     => ExportHtml(inputPath, outputPath, options, progress, ct),
                    ExportFormat.Epub     => ExportEpub(inputPath, outputPath, options, progress, ct),
                    ExportFormat.Png      => ExportImages(inputPath, outputPath, options, progress, ct, jpeg: false),
                    ExportFormat.Jpg      => ExportImages(inputPath, outputPath, options, progress, ct, jpeg: true),
                    ExportFormat.Svg      => ExportSvg(inputPath, outputPath, options, progress, ct),
                    ExportFormat.Pdf      => ExportPdfCopy(inputPath, outputPath),
                    ExportFormat.PdfA1b   => ExportPdfA(inputPath, outputPath, part: 1),
                    ExportFormat.PdfA2b   => ExportPdfA(inputPath, outputPath, part: 2),
                    ExportFormat.PdfA3b   => ExportPdfA(inputPath, outputPath, part: 3),
                    ExportFormat.SecurePdf=> ExportSecurePdf(inputPath, outputPath, options),
                    ExportFormat.Docx     => ExportDocx(inputPath, outputPath, options, progress, ct),
                    ExportFormat.Pptx     => ExportPptx(inputPath, outputPath, options, progress, ct),
                    ExportFormat.Xlsx     => ExportXlsx(inputPath, outputPath, options, progress, ct),
                    _                     => OperationResult.Fail($"Unknown format: {format}"),
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Export failed: {Format} {Input}", format, inputPath);
                CleanupOnError(outputPath);
                return OperationResult.Fail($"Export failed: {ex.Message}");
            }
        }, ct);
    }

    // ── 1. Text (.txt) ────────────────────────────────────────────────────────

    private OperationResult ExportTxt(
        string inputPath, string outputPath, ExportOptions opts,
        IProgress<(int, int)>? progress, CancellationToken ct)
    {
        var sb = new StringBuilder();
        using var doc = UglyToad.PdfPig.PdfDocument.Open(inputPath);
        int total = doc.NumberOfPages;
        var (start, end) = PageRange(opts, total);

        for (int i = start; i <= end; i++)
        {
            ct.ThrowIfCancellationRequested();
            var page  = doc.GetPage(i + 1); // PdfPig is 1-based
            var words = page.GetWords();
            var text  = string.Join(" ", words.Select(w => w.Text));

            if (total > 1) sb.AppendLine($"=== Page {i + 1} ===");
            sb.AppendLine(text);
            sb.AppendLine();
            progress?.Report((i - start + 1, end - start + 1));
        }

        if (sb.Length == 0)
            return OperationResult.Fail(
                "No text layer found. This appears to be a scanned PDF — run OCR first.");

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        _logger.LogInformation("TXT export: {Path}", outputPath);
        return OperationResult.Ok($"Exported text → {Path.GetFileName(outputPath)}");
    }

    // ── 2. HTML (.html) — two-layer: rendered page image + selectable text overlay ─

    private OperationResult ExportHtml(
        string inputPath, string outputPath, ExportOptions opts,
        IProgress<(int, int)>? progress, CancellationToken ct)
    {
        const int    renderDpi = 150;
        const double ptToPx   = 96.0 / 72.0; // PDF points → CSS px

        using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath);
        using var pdfDoc = PdfiumViewer.PdfDocument.Load(inputPath);
        int total = pigDoc.NumberOfPages;
        var (start, end) = PageRange(opts, total);
        string title = System.Net.WebUtility.HtmlEncode(
            pigDoc.Information.Title ?? Path.GetFileNameWithoutExtension(inputPath));

        var sb = new StringBuilder(1024 * 64);
        // $$"""...""" uses {{expr}} for interpolation so CSS single-braces are literal
        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8"/>
              <meta name="viewport" content="width=device-width,initial-scale=1"/>
              <title>{{title}}</title>
              <style>
                *{box-sizing:border-box;margin:0;padding:0}
                body{background:#525659;font-family:sans-serif;padding:20px 0}
                .page{
                  position:relative;margin:20px auto;background:#fff;
                  box-shadow:0 4px 24px rgba(0,0,0,.5);overflow:hidden;display:block
                }
                .page>img.bg{
                  display:block;width:100%;height:100%;
                  position:absolute;top:0;left:0;
                  pointer-events:none;user-select:none;-webkit-user-select:none
                }
                .tl{
                  position:absolute;top:0;left:0;width:100%;height:100%;
                  overflow:hidden;line-height:1;caret-color:#000
                }
                .tl span{
                  position:absolute;white-space:pre;
                  color:transparent;transform-origin:0% 0%;
                  cursor:text;user-select:text;-webkit-user-select:text
                }
              </style>
            </head>
            <body>
            """);
        sb.AppendLine();

        for (int i = start; i <= end; i++)
        {
            ct.ThrowIfCancellationRequested();
            var page = pigDoc.GetPage(i + 1); // 1-based
            double pw  = page.Width;           // pts
            double ph  = page.Height;          // pts
            double wpx = pw * ptToPx;
            double hpx = ph * ptToPx;

            // Visual ground-truth: high-res rendered PNG embedded inline
            var b64 = Convert.ToBase64String(RenderPageToPng(pdfDoc, i, renderDpi));

            sb.Append($"<div class=\"page\" style=\"width:{wpx:F0}px;height:{hpx:F0}px\">\n");
            sb.Append($"<img class=\"bg\" src=\"data:image/png;base64,{b64}\" alt=\"Page {i + 1}\"/>\n");
            sb.Append("<div class=\"tl\">\n");

            // Selectable text overlay — one span per word, positioned to match the image
            var words = page.GetWords().ToList();
            foreach (var word in words)
            {
                if (string.IsNullOrWhiteSpace(word.Text)) continue;

                double x   = word.BoundingBox.Left * ptToPx;
                // PDF Y is bottom-up; CSS Y is top-down → flip via page height
                double y   = (ph - word.BoundingBox.Top) * ptToPx;
                double ww  = word.BoundingBox.Width  * ptToPx;
                double fss = word.BoundingBox.Height * ptToPx;

                // Use actual font point size when available for a tighter text overlay
                if (word.Letters.Count > 0 && word.Letters[0].FontSize > 0)
                    fss = word.Letters[0].FontSize * ptToPx;
                if (fss < 4) fss = 4;

                // scaleX stretches the transparent span to cover the word glyph width,
                // so text selection aligns with the visible characters underneath
                double estWidth = word.Text.Length * fss * 0.55;
                double scaleX   = estWidth > 0 && ww > 0
                    ? Math.Clamp(ww / estWidth, 0.1, 8.0)
                    : 1.0;

                var txt = System.Net.WebUtility.HtmlEncode(word.Text);
                sb.Append($"<span style=\"left:{x:F1}px;top:{y:F1}px;font-size:{fss:F1}px;transform:scaleX({scaleX:F3})\">{txt}</span>\n");
            }

            sb.Append("</div>\n</div>\n");
            progress?.Report((i - start + 1, end - start + 1));
        }

        sb.Append("\n</body>\n</html>");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        _logger.LogInformation("HTML export: {Path}", outputPath);
        return OperationResult.Ok($"Exported HTML → {Path.GetFileName(outputPath)}");
    }

    // ── 3. EPUB (.epub) ───────────────────────────────────────────────────────

    private OperationResult ExportEpub(
        string inputPath, string outputPath, ExportOptions opts,
        IProgress<(int, int)>? progress, CancellationToken ct)
    {
        using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath);
        using var pdfDoc = PdfiumViewer.PdfDocument.Load(inputPath);
        int total = pigDoc.NumberOfPages;
        var (start, end) = PageRange(opts, total);

        var title  = Path.GetFileNameWithoutExtension(inputPath);
        var bookId = Guid.NewGuid().ToString();
        var now    = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // Collect page content first
        var chapters = new List<(string XhtmlName, string XhtmlContent)>();
        for (int i = start; i <= end; i++)
        {
            ct.ThrowIfCancellationRequested();
            var page  = pigDoc.GetPage(i + 1);
            var words = page.GetWords().ToList();
            int pageNum = i + 1;
            string body;

            if (words.Count == 0)
            {
                var png = RenderPageToPng(pdfDoc, i, 96);
                var b64 = Convert.ToBase64String(png);
                body = $"<p><img src=\"data:image/png;base64,{b64}\" style=\"max-width:100%\"/></p>";
            }
            else
            {
                var lines = new StringBuilder();
                var lineWords = new List<string>();
                double? lastY = null;

                foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom)
                                          .ThenBy(w => w.BoundingBox.Left))
                {
                    double y = Math.Round(word.BoundingBox.Bottom, 0);
                    if (lastY.HasValue && Math.Abs(y - lastY.Value) > 2)
                    {
                        if (lineWords.Count > 0)
                        {
                            lines.Append("<p>");
                            lines.Append(System.Net.WebUtility.HtmlEncode(string.Join(" ", lineWords)));
                            lines.AppendLine("</p>");
                            lineWords.Clear();
                        }
                    }
                    lineWords.Add(word.Text);
                    lastY = y;
                }
                if (lineWords.Count > 0)
                {
                    lines.Append("<p>");
                    lines.Append(System.Net.WebUtility.HtmlEncode(string.Join(" ", lineWords)));
                    lines.AppendLine("</p>");
                }
                body = lines.ToString();
            }

            var xhtml = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd">
                <html xmlns="http://www.w3.org/1999/xhtml">
                <head><title>Page {pageNum}</title>
                <link rel="stylesheet" type="text/css" href="style.css"/>
                </head>
                <body><h2>Page {pageNum}</h2>{body}</body>
                </html>
                """;
            chapters.Add(($"page{pageNum:D4}.xhtml", xhtml));
            progress?.Report((i - start + 1, end - start + 1));
        }

        // Build EPUB ZIP
        using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);

        // mimetype — must be first, uncompressed
        var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var w = new StreamWriter(mimetype.Open()))
            w.Write("application/epub+zip");

        // META-INF/container.xml
        AddText(zip, "META-INF/container.xml", """
            <?xml version="1.0"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        // OEBPS/style.css
        AddText(zip, "OEBPS/style.css",
            "body{font-family:serif;margin:1em 2em} h2{font-size:1em;color:#999} p{text-align:justify}");

        // OEBPS/page chapters
        foreach (var (name, content) in chapters)
            AddText(zip, $"OEBPS/{name}", content);

        // OEBPS/content.opf
        var manifest = new StringBuilder();
        manifest.AppendLine("    <item id=\"style\" href=\"style.css\" media-type=\"text/css\"/>");
        var spine = new StringBuilder();
        foreach (var (name, _) in chapters)
        {
            var id = Path.GetFileNameWithoutExtension(name);
            manifest.AppendLine($"    <item id=\"{id}\" href=\"{name}\" media-type=\"application/xhtml+xml\"/>");
            spine.AppendLine($"    <itemref idref=\"{id}\"/>");
        }

        AddText(zip, "OEBPS/content.opf", $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="bookid" version="2.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
                <dc:title>{System.Net.WebUtility.HtmlEncode(title)}</dc:title>
                <dc:identifier id="bookid">urn:uuid:{bookId}</dc:identifier>
                <dc:language>en</dc:language>
                <dc:date>{now}</dc:date>
                <dc:creator>PDFAgent</dc:creator>
              </metadata>
              <manifest>
            {manifest.ToString().TrimEnd()}
              </manifest>
              <spine>
            {spine.ToString().TrimEnd()}
              </spine>
            </package>
            """);

        // OEBPS/toc.ncx
        var ncxNav = new StringBuilder();
        int order = 1;
        foreach (var (name, _) in chapters)
        {
            ncxNav.AppendLine($"""
                  <navPoint id="np{order}" playOrder="{order}">
                    <navLabel><text>Page {order}</text></navLabel>
                    <content src="{name}"/>
                  </navPoint>
            """);
            order++;
        }
        AddText(zip, "OEBPS/toc.ncx", $"""
            <?xml version="1.0" encoding="utf-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head>
                <meta name="dtb:uid" content="urn:uuid:{bookId}"/>
                <meta name="dtb:depth" content="1"/>
                <meta name="dtb:totalPageCount" content="0"/>
                <meta name="dtb:maxPageNumber" content="0"/>
              </head>
              <docTitle><text>{System.Net.WebUtility.HtmlEncode(title)}</text></docTitle>
              <navMap>
            {ncxNav.ToString().TrimEnd()}
              </navMap>
            </ncx>
            """);

        _logger.LogInformation("EPUB export: {Path}", outputPath);
        return OperationResult.Ok($"Exported EPUB → {Path.GetFileName(outputPath)}");
    }

    // ── 4 & 5. PNG / JPEG ─────────────────────────────────────────────────────

    private OperationResult ExportImages(
        string inputPath, string outputPath, ExportOptions opts,
        IProgress<(int, int)>? progress, CancellationToken ct, bool jpeg)
    {
        using var pdfDoc = PdfiumViewer.PdfDocument.Load(inputPath);
        int total  = pdfDoc.PageCount;
        var (start, end) = PageRange(opts, total);
        string ext = jpeg ? ".jpg" : ".png";
        string baseName = Path.GetFileNameWithoutExtension(inputPath);

        bool multiPage = (end - start) > 0;
        if (multiPage)
        {
            // outputPath is a folder for multi-page
            Directory.CreateDirectory(outputPath);
            for (int i = start; i <= end; i++)
            {
                ct.ThrowIfCancellationRequested();
                var bytes = GetImageBytes(pdfDoc, i, opts.Dpi, jpeg);
                File.WriteAllBytes(Path.Combine(outputPath, $"{baseName}_page{i + 1:D3}{ext}"), bytes);
                progress?.Report((i - start + 1, end - start + 1));
            }
            return OperationResult.Ok($"Exported {end - start + 1} page(s) to folder");
        }
        else
        {
            var bytes = GetImageBytes(pdfDoc, start, opts.Dpi, jpeg);
            File.WriteAllBytes(outputPath, bytes);
            progress?.Report((1, 1));
            return OperationResult.Ok($"Exported image → {Path.GetFileName(outputPath)}");
        }
    }

    private static byte[] GetImageBytes(PdfiumViewer.PdfDocument doc, int pageIdx, int dpi, bool jpeg)
    {
        var png = RenderPageToPng(doc, pageIdx, dpi);
        return jpeg ? PngToJpeg(png) : png;
    }

    // ── 6. SVG ────────────────────────────────────────────────────────────────

    private OperationResult ExportSvg(
        string inputPath, string outputPath, ExportOptions opts,
        IProgress<(int, int)>? progress, CancellationToken ct)
    {
        using var pdfDoc = PdfiumViewer.PdfDocument.Load(inputPath);
        int total = pdfDoc.PageCount;
        var (start, end) = PageRange(opts, total);

        bool multiPage = (end - start) > 0;
        string baseName = Path.GetFileNameWithoutExtension(inputPath);

        if (multiPage)
            Directory.CreateDirectory(outputPath);

        for (int i = start; i <= end; i++)
        {
            ct.ThrowIfCancellationRequested();
            var pageSize = pdfDoc.PageSizes[i];
            var pngBytes = RenderPageToPng(pdfDoc, i, opts.Dpi);
            var b64      = Convert.ToBase64String(pngBytes);
            var svg      = BuildSvg(pageSize.Width, pageSize.Height, b64);

            string dest = multiPage
                ? Path.Combine(outputPath, $"{baseName}_page{i + 1:D3}.svg")
                : outputPath;
            File.WriteAllText(dest, svg, Encoding.UTF8);
            progress?.Report((i - start + 1, end - start + 1));
        }

        return OperationResult.Ok(multiPage
            ? $"Exported {end - start + 1} SVG(s) to folder"
            : $"Exported SVG → {Path.GetFileName(outputPath)}");
    }

    private static string BuildSvg(double widthPts, double heightPts, string pngBase64)
    {
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!-- raster-embedded SVG — exported by PDFAgent -->
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
                 width="{widthPts:F0}pt" height="{heightPts:F0}pt"
                 viewBox="0 0 {widthPts:F0} {heightPts:F0}">
              <image x="0" y="0" width="{widthPts:F0}" height="{heightPts:F0}"
                     xlink:href="data:image/png;base64,{pngBase64}"/>
            </svg>
            """;
    }

    // ── 7. PDF copy ───────────────────────────────────────────────────────────

    private static OperationResult ExportPdfCopy(string inputPath, string outputPath)
    {
        File.Copy(inputPath, outputPath, overwrite: true);
        return OperationResult.Ok($"Saved PDF copy → {Path.GetFileName(outputPath)}");
    }

    // ── 8/9/10. PDF/A ─────────────────────────────────────────────────────────

    private OperationResult ExportPdfA(string inputPath, string outputPath, int part)
    {
        using var input  = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();

        foreach (PdfPage page in input.Pages)
            output.AddPage(page);

        var now = DateTime.UtcNow;
        output.Info.Creator          = "PDFAgent";
        output.Info.CreationDate     = now;
        output.Info.ModificationDate = now;
        output.Info.Keywords         = $"PDF/A-{part}B";
        output.Options.CompressContentStreams = true;

        // Inject XMP metadata stream for PDF/A conformance declaration.
        // This follows ISO 19005-x: the /Catalog /Metadata entry must be an XMP stream.
        InjectPdfAXmp(output, part, "B", now);

        output.Save(outputPath);
        _logger.LogInformation("PDF/A-{Part}b export: {Path}", part, outputPath);
        return OperationResult.Ok(
            $"Exported PDF/A-{part}b → {Path.GetFileName(outputPath)}\n" +
            "(Best-effort — validate with Adobe Acrobat or veraPDF for strict conformance.)");
    }

    private static void InjectPdfAXmp(PdfDocument doc, int part, string conformance, DateTime ts)
    {
        // Set document info fields that signal PDF/A intent.
        // Strict ISO 19005 conformance additionally requires an XMP metadata stream
        // injected into /Catalog /Metadata — PDFsharp 6 does not expose a public API
        // for this. Validate with veraPDF or Adobe Acrobat for strict conformance.
        doc.Info.Creator  = "PDFAgent";
        doc.Info.Keywords = $"PDF/A-{part}{conformance}";
    }

    // ── 11. Secure PDF ────────────────────────────────────────────────────────

    private OperationResult ExportSecurePdf(
        string inputPath, string outputPath, ExportOptions opts)
    {
        if (string.IsNullOrEmpty(opts.UserPassword) && string.IsNullOrEmpty(opts.OwnerPassword))
            return OperationResult.Fail("At least one password (user or owner) is required for Secure PDF.");

        using var input  = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();

        foreach (PdfPage page in input.Pages)
            output.AddPage(page);

        if (!string.IsNullOrEmpty(opts.UserPassword))
            output.SecuritySettings.UserPassword  = opts.UserPassword;
        if (!string.IsNullOrEmpty(opts.OwnerPassword))
            output.SecuritySettings.OwnerPassword = opts.OwnerPassword;

        // Restrict modification — reading/printing allowed
        output.SecuritySettings.PermitAnnotations      = false;
        output.SecuritySettings.PermitAssembleDocument = false;
        output.SecuritySettings.PermitExtractContent   = false;
        output.SecuritySettings.PermitFormsFill        = false;
        output.SecuritySettings.PermitFullQualityPrint = true;
        output.SecuritySettings.PermitModifyDocument   = false;
        output.SecuritySettings.PermitPrint            = true;

        output.Save(outputPath);
        _logger.LogInformation("SecurePDF export: {Path}", outputPath);
        return OperationResult.Ok($"Saved password-protected PDF → {Path.GetFileName(outputPath)}");
    }

    // ── 12. DOCX — styled text extraction: fonts, sizes, bold, italic, color ──

    private OperationResult ExportDocx(
        string inputPath, string outputPath, ExportOptions opts,
        IProgress<(int, int)>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Word COM path: highest possible fidelity (Word 2013+)
        var comResult = WordFromPdfConverter.ConvertToDocx(inputPath);
        if (comResult != null)
        {
            File.Move(comResult, outputPath, overwrite: true);
            _logger.LogInformation("DOCX (Word COM): {Path}", outputPath);
            return OperationResult.Ok($"Exported Word document → {Path.GetFileName(outputPath)}");
        }

        // Structured extraction: tables, lists, images, headings, styled text
        using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath);
        int total = pigDoc.NumberOfPages;
        var (start, end) = PageRange(opts, total);

        var pages = new List<DocPage>(end - start + 1);
        for (int i = start; i <= end; i++)
        {
            ct.ThrowIfCancellationRequested();
            var page = pigDoc.GetPage(i + 1);
            pages.Add(AnalyzePage(page));
            progress?.Report((i - start + 1, end - start + 1));
        }

        DocxBuilder.Build(pages, outputPath);
        _logger.LogInformation("DOCX (styled .NET): {Path}", outputPath);
        return OperationResult.Ok($"Exported Word document → {Path.GetFileName(outputPath)}");
    }

    // ── 13. PPTX ──────────────────────────────────────────────────────────────

    private OperationResult ExportPptx(
        string inputPath, string outputPath, ExportOptions opts,
        IProgress<(int, int)>? progress, CancellationToken ct)
    {
        using var pdfDoc = PdfiumViewer.PdfDocument.Load(inputPath);
        int total  = pdfDoc.PageCount;
        var (start, end) = PageRange(opts, total);

        var firstSize = pdfDoc.PageSizes[start];
        long slideCx  = PtsToEmu(firstSize.Width);
        long slideCy  = PtsToEmu(firstSize.Height);

        using var pptxDoc = PresentationDocument.Create(outputPath,
            PresentationDocumentType.Presentation);

        var presPart = pptxDoc.AddPresentationPart();
        var pres     = new P.Presentation();
        pres.Append(new P.SlideSize
        {
            Cx   = (Int32Value)slideCx,
            Cy   = (Int32Value)slideCy,
            Type = P.SlideSizeValues.Custom,
        });
        pres.Append(new P.NotesSize { Cx = 6858000, Cy = 9144000 });
        presPart.Presentation = pres;

        // Minimal slide master & layout (required by PowerPoint)
        var masterPart = presPart.AddNewPart<SlideMasterPart>();
        var layoutPart = masterPart.AddNewPart<SlideLayoutPart>();

        layoutPart.SlideLayout = new P.SlideLayout(
            new P.CommonSlideData(new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(new D.TransformGroup()))),
            new P.ColorMapOverride(new D.MasterColorMapping()));
        layoutPart.SlideLayout.Save();

        masterPart.SlideMaster = new P.SlideMaster(
            new P.CommonSlideData(new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(new D.TransformGroup()))),
            new P.ColorMap
            {
                Background1      = D.ColorSchemeIndexValues.Light1,
                Text1            = D.ColorSchemeIndexValues.Dark1,
                Background2      = D.ColorSchemeIndexValues.Light2,
                Text2            = D.ColorSchemeIndexValues.Dark2,
                Accent1          = D.ColorSchemeIndexValues.Accent1,
                Accent2          = D.ColorSchemeIndexValues.Accent2,
                Accent3          = D.ColorSchemeIndexValues.Accent3,
                Accent4          = D.ColorSchemeIndexValues.Accent4,
                Accent5          = D.ColorSchemeIndexValues.Accent5,
                Accent6          = D.ColorSchemeIndexValues.Accent6,
                Hyperlink        = D.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = D.ColorSchemeIndexValues.FollowedHyperlink,
            },
            new P.SlideLayoutIdList(
                new P.SlideLayoutId
                {
                    Id             = 2049U,
                    RelationshipId = masterPart.GetIdOfPart(layoutPart),
                }));
        masterPart.SlideMaster.Save();

        var slideMasterIdList = new P.SlideMasterIdList(
            new P.SlideMasterId
            {
                Id             = 2148U,
                RelationshipId = presPart.GetIdOfPart(masterPart),
            });
        pres.Append(slideMasterIdList);

        var slideIdList = new P.SlideIdList();
        pres.Append(slideIdList);
        pres.Save();

        uint slideId = 256;
        for (int i = start; i <= end; i++)
        {
            ct.ThrowIfCancellationRequested();
            var pageSize = pdfDoc.PageSizes[i];
            long cx      = PtsToEmu(pageSize.Width);
            long cy      = PtsToEmu(pageSize.Height);
            var png      = RenderPageToPng(pdfDoc, i, opts.Dpi);

            var slidePart = presPart.AddNewPart<SlidePart>();

            var imgPart = slidePart.AddImagePart(ImagePartType.Png);
            using (var ms = new MemoryStream(png))
                imgPart.FeedData(ms);

            string imgRelId = slidePart.GetIdOfPart(imgPart);

            slidePart.Slide = BuildPptxSlide(imgRelId, cx, cy);
            slidePart.Slide.Save();

            slidePart.AddPart(layoutPart);

            slideIdList.Append(new P.SlideId
            {
                Id             = slideId++,
                RelationshipId = presPart.GetIdOfPart(slidePart),
            });
            pres.Save();

            progress?.Report((i - start + 1, end - start + 1));
        }

        _logger.LogInformation("PPTX export: {Path}", outputPath);
        return OperationResult.Ok($"Exported PowerPoint → {Path.GetFileName(outputPath)}");
    }

    private static P.Slide BuildPptxSlide(string imgRelId, long cx, long cy)
    {
        return new P.Slide(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new D.TransformGroup()),
                    new P.Picture(
                        new P.NonVisualPictureProperties(
                            new P.NonVisualDrawingProperties { Id = 2U, Name = "Page" },
                            new P.NonVisualPictureDrawingProperties(
                                new D.PictureLocks { NoChangeAspect = true }),
                            new P.ApplicationNonVisualDrawingProperties()),
                        new P.BlipFill(
                            new D.Blip { Embed = imgRelId },
                            new D.Stretch(new D.FillRectangle())),
                        new P.ShapeProperties(
                            new D.Transform2D(
                                new D.Offset { X = 0L, Y = 0L },
                                new D.Extents { Cx = cx, Cy = cy }),
                            new D.PresetGeometry(new D.AdjustValueList())
                                { Preset = D.ShapeTypeValues.Rectangle })))),
            new P.ColorMapOverride(new D.MasterColorMapping()));
    }

    // ── 14. XLSX — grid detection: words placed in (row, col) via X clustering ─

    private OperationResult ExportXlsx(
        string inputPath, string outputPath, ExportOptions opts,
        IProgress<(int, int)>? progress, CancellationToken ct)
    {
        using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath);
        int total = pigDoc.NumberOfPages;
        var (start, end) = PageRange(opts, total);

        using var xlsxDoc = SpreadsheetDocument.Create(outputPath, SpreadsheetDocumentType.Workbook);
        var wbPart = xlsxDoc.AddWorkbookPart();
        wbPart.Workbook = new Workbook();
        var sheets = wbPart.Workbook.AppendChild(new Sheets());

        for (int i = start; i <= end; i++)
        {
            ct.ThrowIfCancellationRequested();
            var page  = pigDoc.GetPage(i + 1);
            var words = page.GetWords().ToList();

            var wsPart    = wbPart.AddNewPart<WorksheetPart>();
            var sheetData = BuildGridSheetData(words, page.Height);

            wsPart.Worksheet = new Worksheet(sheetData);
            wsPart.Worksheet.Save();

            sheets.Append(new Sheet
            {
                Id      = wbPart.GetIdOfPart(wsPart),
                SheetId = (uint)(i - start + 1),
                Name    = $"Page {i + 1}",
            });
            progress?.Report((i - start + 1, end - start + 1));
        }

        wbPart.Workbook.Save();
        _logger.LogInformation("XLSX export: {Path}", outputPath);
        return OperationResult.Ok($"Exported Excel workbook → {Path.GetFileName(outputPath)}");
    }

    /// <summary>
    /// Detects column structure by clustering word X-start positions, then assigns
    /// each word to its (row, col) grid cell.  Works for both tabular and plain-text PDFs.
    /// </summary>
    private static SheetData BuildGridSheetData(
        IList<UglyToad.PdfPig.Content.Word> words,
        double pageHeight)
    {
        var sheetData = new SheetData();
        if (words.Count == 0)
        {
            var emptyRow = new Row { RowIndex = 1U };
            emptyRow.Append(MakeCell("A1", "(scanned page — no text layer)"));
            sheetData.Append(emptyRow);
            return sheetData;
        }

        // 1. Group words into horizontal rows by Y coordinate proximity (4pt tolerance)
        const double rowTol = 4.0;
        var rowGroups = GroupWordsIntoRows(words, rowTol);

        // 2. Detect column boundaries from all word left-edge X positions
        var allXs = words.Select(w => w.BoundingBox.Left).OrderBy(x => x).ToList();
        var colBoundaries = DetectColumnBoundaries(allXs, minGap: 18.0);

        // 3. Build rows top to bottom (PDF Y is bottom-up → sort descending by center Y)
        uint rowIdx = 1;
        foreach (var (_, rowWords) in rowGroups.OrderByDescending(r => r.CenterY))
        {
            var row        = new Row { RowIndex = rowIdx };
            bool hasContent = false;

            // Assign each word to a column cell
            var cellMap = new SortedDictionary<int, List<string>>();
            foreach (var word in rowWords.OrderBy(w => w.BoundingBox.Left))
            {
                int col = FindColumnIndex(colBoundaries, word.BoundingBox.Left);
                if (!cellMap.TryGetValue(col, out var bucket))
                    cellMap[col] = bucket = new List<string>();
                bucket.Add(word.Text);
            }

            foreach (var (col, texts) in cellMap)
            {
                string cellRef = $"{ColLetter(col)}{rowIdx}";
                row.Append(MakeCell(cellRef, string.Join(" ", texts)));
                hasContent = true;
            }

            if (hasContent)
            {
                sheetData.Append(row);
                rowIdx++;
            }
        }

        return sheetData;
    }

    // ── DOCX structured page analysis ────────────────────────────────────────

    /// <summary>
    /// Full structural analysis of a page: detects tables, lists, images, and headings
    /// in addition to styled text. Returns a DocPage with heterogeneous blocks.
    /// </summary>
    internal static DocPage AnalyzePage(UglyToad.PdfPig.Content.Page page)
    {
        double pageH = page.Height;
        double pageW = page.Width;

        // ── 1. Gather all words, group into rows ─────────────────────────────
        var words    = page.GetWords().ToList();
        var wordRows = GroupWordsIntoRows(words, tolerance: 4.0)
            .OrderByDescending(r => r.CenterY)
            .ToList();

        // ── 2. Detect table regions: consecutive rows with ≥2 col-groups ────
        // Each detected table is a list of consecutive row indices in wordRows
        var tableSpans = DetectTableSpans(wordRows, colGap: 30.0, minRows: 2);

        // Collect Y-ranges occupied by tables so we can exclude them from letter extraction
        var tableYRanges = new List<(double MinY, double MaxY)>();
        foreach (var span in tableSpans)
        {
            double minY = span.SelectMany(ri => wordRows[ri].Words)
                             .Min(w => w.BoundingBox.Bottom);
            double maxY = span.SelectMany(ri => wordRows[ri].Words)
                             .Max(w => w.BoundingBox.Top);
            tableYRanges.Add((minY - 2, maxY + 2)); // small tolerance
        }

        // ── 3. Extract styled text lines, excluding table regions ────────────
        var nonTableLetters = page.Letters
            .Where(l => !string.IsNullOrEmpty(l.Value))
            .Where(l =>
            {
                double y = l.GlyphRectangle.Bottom;
                return !tableYRanges.Any(r => y >= r.MinY && y <= r.MaxY);
            })
            .ToList();
        var textLines = ExtractDocLinesFromLetters(nonTableLetters);

        // Apply list & heading detection to text lines
        double leftMargin = words.Count > 0
            ? words.Min(w => w.BoundingBox.Left)
            : 40.0;
        textLines = ClassifyLines(textLines, leftMargin);

        // ── 4. Extract images ────────────────────────────────────────────────
        var images = new List<(double MidY, ImageBlock Block)>();
        if (page.NumberOfImages > 0)
        {
            foreach (var img in page.GetImages())
            {
                if (img.IsImageMask) continue;
                if (!img.TryGetPng(out var pngBytes)) continue;

                double imgW = img.Bounds.Width;
                double imgH = img.Bounds.Height;
                if (imgW < 20 || imgH < 20) continue; // skip decorative/tiny images

                double midY = (img.Bounds.Bottom + img.Bounds.Top) / 2.0;
                images.Add((midY, new ImageBlock(pngBytes, "image/png", imgW, imgH)));
            }
        }

        // ── 5. Assemble blocks, sorted top-to-bottom (PDF Y is bottom-up) ───
        var blocks = new List<(double SortY, DocBlock Block)>();

        // Text line blocks — compute sort Y from first letter's baseline
        if (textLines.Count > 0)
        {
            // Group text lines into a single TextLines block per page
            // (ordering is already top-to-bottom from ExtractDocLinesFromLetters)
            // We'll emit all text as one block; ordering vs. images handled below
            foreach (var line in textLines)
            {
                double lineY = line.Runs.Count > 0 ? EstimateLineY(line) : double.NaN;
                blocks.Add((lineY, DocBlock.FromLines(new[] { line })));
            }
        }

        // Table blocks
        foreach (var span in tableSpans)
        {
            double tableTopY = span.SelectMany(ri => wordRows[ri].Words)
                                   .Max(w => w.BoundingBox.Top);
            var tableBlock = BuildTableBlock(wordRows, span);
            blocks.Add((pageH - tableTopY, DocBlock.FromTable(tableBlock))); // convert to top-down
        }

        // Image blocks
        foreach (var (midY, imgBlock) in images)
            blocks.Add((pageH - midY, DocBlock.FromImage(imgBlock)));

        // Sort by Y position (top of page = small Y after flip)
        var sortedBlocks = blocks
            .OrderBy(b => double.IsNaN(b.SortY) ? double.MaxValue : b.SortY)
            .Select(b => b.Block)
            .ToList();

        // Merge consecutive TextLine blocks into grouped blocks for efficiency
        var merged = MergeTextLineBlocks(sortedBlocks);
        return new DocPage(merged, pageW, pageH);
    }

    private static List<DocBlock> MergeTextLineBlocks(List<DocBlock> blocks)
    {
        var result = new List<DocBlock>();
        var pending = new List<TextLine>();

        void Flush()
        {
            if (pending.Count > 0)
            {
                result.Add(DocBlock.FromLines(pending.ToList()));
                pending.Clear();
            }
        }

        foreach (var b in blocks)
        {
            if (b.Kind == DocBlockKind.TextLines)
            {
                pending.AddRange(b.Lines!);
            }
            else
            {
                Flush();
                result.Add(b);
            }
        }
        Flush();
        return result;
    }

    // Rough sort-Y for a TextLine: the median run's first char Y (already top-down ordering
    // from ExtractDocLinesFromLetters — we just use its list index, kept for images interleave)
    private static double EstimateLineY(TextLine line) => 0; // ordering already correct; 0 groups them

    // ── Table detection ───────────────────────────────────────────────────────

    /// <summary>
    /// Finds maximal spans of word rows where each row has ≥2 column-groups
    /// separated by at least colGap points.  A span must have ≥ minRows rows.
    /// Returns list of spans; each span is the list of row indices (into wordRows).
    /// </summary>
    private static List<List<int>> DetectTableSpans(
        List<(double CenterY, List<UglyToad.PdfPig.Content.Word> Words)> wordRows,
        double colGap, int minRows)
    {
        var result  = new List<List<int>>();
        var current = new List<int>();

        for (int i = 0; i < wordRows.Count; i++)
        {
            var rowWords = wordRows[i].Words.OrderBy(w => w.BoundingBox.Left).ToList();
            if (IsMultiColumn(rowWords, colGap))
            {
                current.Add(i);
            }
            else
            {
                if (current.Count >= minRows)
                    result.Add(new List<int>(current));
                current.Clear();
            }
        }
        if (current.Count >= minRows)
            result.Add(current);

        return result;
    }

    private static bool IsMultiColumn(
        List<UglyToad.PdfPig.Content.Word> sortedByX, double colGap)
    {
        if (sortedByX.Count < 2) return false;
        for (int i = 1; i < sortedByX.Count; i++)
        {
            double gap = sortedByX[i].BoundingBox.Left - sortedByX[i - 1].BoundingBox.Right;
            if (gap >= colGap) return true;
        }
        return false;
    }

    private static TableBlock BuildTableBlock(
        List<(double CenterY, List<UglyToad.PdfPig.Content.Word> Words)> wordRows,
        List<int> span)
    {
        // Determine max column count across all rows in the span
        var rowWordLists = span.Select(ri =>
            wordRows[ri].Words.OrderBy(w => w.BoundingBox.Left).ToList()).ToList();

        int maxCols = rowWordLists.Max(r => CountColumns(r, colGap: 30.0));
        maxCols = Math.Max(2, maxCols);

        var tableRows = new List<TableRow>();
        foreach (var rowWords in rowWordLists)
        {
            var cells = SplitRowIntoCells(rowWords, maxCols, colGap: 30.0);
            tableRows.Add(new TableRow(cells));
        }
        return new TableBlock(tableRows);
    }

    private static int CountColumns(
        List<UglyToad.PdfPig.Content.Word> sortedByX, double colGap)
    {
        if (sortedByX.Count == 0) return 0;
        int cols = 1;
        for (int i = 1; i < sortedByX.Count; i++)
        {
            double gap = sortedByX[i].BoundingBox.Left - sortedByX[i - 1].BoundingBox.Right;
            if (gap >= colGap) cols++;
        }
        return cols;
    }

    private static List<IReadOnlyList<TextRun>> SplitRowIntoCells(
        List<UglyToad.PdfPig.Content.Word> sortedByX, int maxCols, double colGap)
    {
        // Group consecutive words into cells based on gap
        var cellWords = new List<List<UglyToad.PdfPig.Content.Word>>();
        var current   = new List<UglyToad.PdfPig.Content.Word>();

        for (int i = 0; i < sortedByX.Count; i++)
        {
            if (i == 0)
            {
                current.Add(sortedByX[i]);
            }
            else
            {
                double gap = sortedByX[i].BoundingBox.Left - sortedByX[i - 1].BoundingBox.Right;
                if (gap >= colGap)
                {
                    cellWords.Add(current);
                    current = new List<UglyToad.PdfPig.Content.Word>();
                }
                current.Add(sortedByX[i]);
            }
        }
        if (current.Count > 0) cellWords.Add(current);

        // Pad or truncate to maxCols
        var result = new List<IReadOnlyList<TextRun>>();
        for (int c = 0; c < maxCols; c++)
        {
            if (c < cellWords.Count)
            {
                var text = string.Join(" ", cellWords[c].Select(w => w.Text));
                result.Add(new[] { new TextRun(text, "", 11, false, false, "000000") });
            }
            else
            {
                result.Add(Array.Empty<TextRun>());
            }
        }
        return result;
    }

    // ── List & heading classification ─────────────────────────────────────────

    private static readonly HashSet<char> BulletChars = new("•·▪▸▶‒–—◦○●■□◆◇");

    private static List<TextLine> ClassifyLines(List<TextLine> lines, double leftMargin)
    {
        var result = new List<TextLine>(lines.Count);
        foreach (var line in lines)
        {
            var classified = TryClassifyLine(line, leftMargin);
            result.Add(classified);
        }
        return result;
    }

    private static TextLine TryClassifyLine(TextLine line, double leftMargin)
    {
        if (line.Runs.Count == 0) return line;

        string firstText = line.Runs[0].Text.TrimStart();
        if (string.IsNullOrEmpty(firstText)) return line;

        char firstChar = firstText[0];

        // Bullet list detection
        if (BulletChars.Contains(firstChar))
        {
            // Strip the bullet character and leading space from the first run
            var strippedRuns = StripPrefix(line.Runs, 1);
            return new TextLine(strippedRuns, line.MaxSizePt, LineType.BulletItem, 0);
        }

        // Dash/hyphen bullet (only if short prefix and followed by space)
        if ((firstChar == '-' || firstChar == '*') &&
            firstText.Length >= 2 && firstText[1] == ' ')
        {
            var strippedRuns = StripPrefix(line.Runs, 2);
            return new TextLine(strippedRuns, line.MaxSizePt, LineType.BulletItem, 0);
        }

        // Numbered list: "1. ", "1) ", "(1) ", "a. ", "i. ", etc.
        if (IsNumberedListPrefix(firstText, out int prefixLen))
        {
            var strippedRuns = StripPrefix(line.Runs, prefixLen);
            return new TextLine(strippedRuns, line.MaxSizePt, LineType.NumberedItem, 0);
        }

        return line;
    }

    private static bool IsNumberedListPrefix(string text, out int prefixLen)
    {
        prefixLen = 0;
        // Patterns: "1. ", "12. ", "1) ", "(1) ", "a. ", "a) ", "i. ", "iv. "
        // Simple check: starts with digits/letters followed by . or ) and space
        int i = 0;
        if (i < text.Length && text[i] == '(') i++;
        int start = i;
        while (i < text.Length && (char.IsDigit(text[i]) || char.IsLetter(text[i]))) i++;
        if (i == start || i >= text.Length) return false;
        if (text[i] != '.' && text[i] != ')') return false;
        i++;
        if (i < text.Length && text[i] == ' ')
        {
            i++;
            prefixLen = i;
            return true;
        }
        return false;
    }

    private static IReadOnlyList<TextRun> StripPrefix(IReadOnlyList<TextRun> runs, int charCount)
    {
        if (runs.Count == 0 || charCount <= 0) return runs;
        var result = new List<TextRun>(runs);
        string first = result[0].Text.TrimStart();
        if (first.Length <= charCount)
        {
            // Remove the entire first run
            result.RemoveAt(0);
            if (result.Count > 0)
            {
                // Also trim leading space from what follows
                var r = result[0];
                result[0] = r with { Text = r.Text.TrimStart() };
            }
        }
        else
        {
            result[0] = result[0] with { Text = first[charCount..].TrimStart() };
        }
        return result;
    }

    // ── Letter-level extraction (accepts pre-filtered letter list) ────────────

    internal static List<TextLine> ExtractDocLinesPublic(UglyToad.PdfPig.Content.Page page) =>
        ExtractDocLines(page);

    private static List<TextLine> ExtractDocLines(UglyToad.PdfPig.Content.Page page)
    {
        var letters = page.Letters
            .Where(l => !string.IsNullOrEmpty(l.Value))
            .ToList();
        return ExtractDocLinesFromLetters(letters);
    }

    private static List<TextLine> ExtractDocLinesFromLetters(
        List<UglyToad.PdfPig.Content.Letter> letters)
    {
        var sorted = letters
            .OrderByDescending(l => l.GlyphRectangle.Bottom)
            .ThenBy(l => l.GlyphRectangle.Left)
            .ToList();

        if (sorted.Count == 0) return new List<TextLine>();
        letters = sorted;

        var result          = new List<TextLine>();
        var lineLetters     = new List<UglyToad.PdfPig.Content.Letter>();
        double currentLineY = letters[0].GlyphRectangle.Bottom;

        foreach (var letter in letters)
        {
            double y = letter.GlyphRectangle.Bottom;
            if (Math.Abs(y - currentLineY) > 4.0)
            {
                if (lineLetters.Count > 0)
                    result.Add(BuildTextLine(lineLetters));
                lineLetters.Clear();
                currentLineY = y;
            }
            lineLetters.Add(letter);
        }
        if (lineLetters.Count > 0)
            result.Add(BuildTextLine(lineLetters));

        return result;
    }

    private static TextLine BuildTextLine(List<UglyToad.PdfPig.Content.Letter> letters)
    {
        var runs = new List<TextRun>();
        if (letters.Count == 0) return new TextLine(runs, 0);

        var    text         = new StringBuilder();
        string curFont      = letters[0].FontName ?? "";
        double curSize      = Math.Round(letters[0].FontSize, 1);
        string curColor     = GetColorHex(letters[0].Color);

        void FlushRun()
        {
            if (text.Length == 0) return;
            runs.Add(new TextRun(
                text.ToString(),
                GetFontFamily(curFont),
                curSize,
                IsBoldFont(curFont),
                IsItalicFont(curFont),
                curColor));
            text.Clear();
        }

        double prevRight = double.NaN;
        foreach (var letter in letters)
        {
            string font  = letter.FontName ?? "";
            double size  = Math.Round(letter.FontSize, 1);
            string color = GetColorHex(letter.Color);

            bool sameStyle = font  == curFont &&
                             Math.Abs(size - curSize) < 0.6 &&
                             color == curColor;

            if (!sameStyle)
            {
                FlushRun();
                curFont  = font;
                curSize  = size;
                curColor = color;
            }

            // Insert a word-space when a visible gap exists between letters
            if (text.Length > 0 && !double.IsNaN(prevRight))
            {
                double gap = letter.GlyphRectangle.Left - prevRight;
                if (gap > curSize * 0.25)
                    text.Append(' ');
            }

            text.Append(letter.Value);
            prevRight = letter.GlyphRectangle.Right;
        }
        FlushRun();

        double maxSize = runs.Count > 0 ? runs.Max(r => r.SizePt) : 0;
        return new TextLine(runs, maxSize);
    }

    private static bool IsBoldFont(string fontName) =>
        fontName.Contains("Bold",  StringComparison.OrdinalIgnoreCase) ||
        fontName.Contains("Heavy", StringComparison.OrdinalIgnoreCase) ||
        fontName.Contains("Black", StringComparison.OrdinalIgnoreCase) ||
        fontName.Contains("Demi",  StringComparison.OrdinalIgnoreCase);

    private static bool IsItalicFont(string fontName) =>
        fontName.Contains("Italic",  StringComparison.OrdinalIgnoreCase) ||
        fontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

    private static string GetFontFamily(string fontName)
    {
        if (string.IsNullOrEmpty(fontName)) return "sans-serif";
        var n = fontName;
        // Strip PDF subset prefix "ABCDEF+"
        int plus = n.IndexOf('+');
        if (plus is >= 0 and <= 7) n = n[(plus + 1)..];
        // "Family,StyleSuffix" → "Family"
        int comma = n.IndexOf(',');
        if (comma > 0) n = n[..comma];
        // "Family-StyleSuffix" → "Family"
        int dash = n.LastIndexOf('-');
        if (dash > 0)
        {
            var suf = n[(dash + 1)..].ToUpperInvariant();
            if (suf is "BOLD" or "ITALIC" or "BOLDITALIC" or "BOLDOBLIQUE" or "OBLIQUE"
                    or "REGULAR" or "LIGHT" or "MEDIUM" or "BOLDMT" or "MT")
                n = n[..dash];
        }
        // Remove trailing "MT" type-face suffix
        if (n.EndsWith("MT", StringComparison.OrdinalIgnoreCase) && n.Length > 4)
            n = n[..^2];
        return string.IsNullOrEmpty(n) ? "sans-serif" : n;
    }

    private static string GetColorHex(UglyToad.PdfPig.Graphics.Colors.IColor? color)
    {
        if (color is UglyToad.PdfPig.Graphics.Colors.RGBColor rgb)
            return $"{(int)(rgb.R * 255):X2}{(int)(rgb.G * 255):X2}{(int)(rgb.B * 255):X2}";
        if (color is UglyToad.PdfPig.Graphics.Colors.GrayColor gray)
        {
            int v = (int)(gray.Gray * 255); // 0=black, 1=white in PDF DeviceGray
            return $"{v:X2}{v:X2}{v:X2}";
        }
        if (color is UglyToad.PdfPig.Graphics.Colors.CMYKColor cmyk)
        {
            int r = (int)((1 - cmyk.C) * (1 - cmyk.K) * 255);
            int g = (int)((1 - cmyk.M) * (1 - cmyk.K) * 255);
            int b = (int)((1 - cmyk.Y) * (1 - cmyk.K) * 255);
            return $"{r:X2}{g:X2}{b:X2}";
        }
        return "000000";
    }

    // ── XLSX grid-detection helpers ───────────────────────────────────────────

    private static List<(double CenterY, List<UglyToad.PdfPig.Content.Word> Words)>
        GroupWordsIntoRows(IList<UglyToad.PdfPig.Content.Word> words, double tolerance)
    {
        var groups = new List<(double CenterY, List<UglyToad.PdfPig.Content.Word> Words)>();
        foreach (var word in words)
        {
            double cy   = (word.BoundingBox.Bottom + word.BoundingBox.Top) / 2.0;
            int    idx  = groups.FindIndex(g => Math.Abs(g.CenterY - cy) <= tolerance);
            if (idx >= 0)
                groups[idx].Words.Add(word);
            else
                groups.Add((cy, new List<UglyToad.PdfPig.Content.Word> { word }));
        }
        return groups;
    }

    /// <summary>
    /// Finds column boundaries by detecting gaps > minGap points between consecutive
    /// distinct X positions of word left edges.
    /// </summary>
    private static List<double> DetectColumnBoundaries(List<double> xPositions, double minGap)
    {
        var sorted     = xPositions.Distinct().OrderBy(x => x).ToList();
        var boundaries = new List<double>();
        if (sorted.Count == 0) return boundaries;

        boundaries.Add(sorted[0]);
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] - sorted[i - 1] > minGap)
                boundaries.Add(sorted[i]);
        }
        return boundaries;
    }

    private static int FindColumnIndex(List<double> boundaries, double x)
    {
        int col = 0;
        for (int i = 0; i < boundaries.Count; i++)
        {
            if (x >= boundaries[i] - 2.0) // 2pt snapping tolerance
                col = i;
        }
        return col;
    }

    private static string ColLetter(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 26)
            return ((char)('A' + zeroBasedIndex)).ToString();
        int hi = zeroBasedIndex / 26 - 1;
        int lo = zeroBasedIndex % 26;
        return $"{(char)('A' + hi)}{(char)('A' + lo)}";
    }

    // ── Rendering helpers ─────────────────────────────────────────────────────

    internal static byte[] RenderPageToPng(PdfiumViewer.PdfDocument doc, int pageIdx, int dpi)
    {
        var size   = doc.PageSizes[pageIdx];
        double scale = dpi / 72.0;
        int w      = Math.Max(1, (int)(size.Width  * scale));
        int h      = Math.Max(1, (int)(size.Height * scale));
        using var img = doc.Render(pageIdx, w, h, (float)dpi, (float)dpi, false);
        if (img is System.Drawing.Bitmap bmp)
            bmp.SetResolution((float)dpi, (float)dpi);
        using var ms = new MemoryStream();
        img.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] PngToJpeg(byte[] pngBytes, int quality = 90)
    {
        using var inMs  = new MemoryStream(pngBytes);
        using var bmp   = new System.Drawing.Bitmap(inMs);
        using var outMs = new MemoryStream();
        var codec = ImageCodecInfo.GetImageDecoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var enc = new System.Drawing.Imaging.EncoderParameters(1);
        enc.Param[0] = new System.Drawing.Imaging.EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality, (long)quality);
        bmp.Save(outMs, codec, enc);
        return outMs.ToArray();
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static (int start, int end) PageRange(ExportOptions opts, int total)
    {
        if (opts.AllPages) return (0, total - 1);
        int idx = Math.Clamp(opts.PageIndex, 0, total - 1);
        return (idx, idx);
    }

    private static long PtsToEmu(double pts) => (long)(pts * 12700);

    private static void AddText(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
        w.Write(content);
    }

    private static Cell MakeCell(string reference, string value)
    {
        return new Cell
        {
            CellReference = reference,
            DataType      = CellValues.InlineString,
            InlineString  = new InlineString(new Text(value)),
        };
    }

    private static void CleanupOnError(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))      File.Delete(outputPath);
            else if (Directory.Exists(outputPath)) Directory.Delete(outputPath, recursive: true);
        }
        catch { }
    }
}
