using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WP  = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using A   = DocumentFormat.OpenXml.Drawing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;

namespace PDFAgent.PdfEngine;

/// <summary>A styled text run extracted from the PDF (single font, size, color).</summary>
public sealed record TextRun(
    string Text,
    string FontFamily,
    double SizePt,
    bool   Bold,
    bool   Italic,
    string ColorHex);

/// <summary>Semantic type of a text line, detected from structure analysis.</summary>
public enum LineType { Paragraph, Heading1, Heading2, Heading3, BulletItem, NumberedItem }

/// <summary>One horizontal line of text, made of styled runs.</summary>
public sealed record TextLine(
    IReadOnlyList<TextRun> Runs,
    double MaxSizePt,
    LineType Type        = LineType.Paragraph,
    int      IndentLevel = 0,
    double   BaselineY   = 0);   // PDF bottom-up Y of the baseline; used for paragraph gap detection

/// <summary>One row of a detected table — each element is one cell's styled runs.</summary>
public sealed record TableRow(IReadOnlyList<IReadOnlyList<TextRun>> Cells);

/// <summary>A table block extracted from the PDF.</summary>
public sealed record TableBlock(IReadOnlyList<TableRow> Rows);

/// <summary>An image block extracted from the PDF.</summary>
public sealed record ImageBlock(byte[] Data, string MediaType, double WidthPt, double HeightPt);

public enum DocBlockKind { TextLines, Table, Image }

/// <summary>A heterogeneous block of content on a PDF page.</summary>
public sealed record DocBlock
{
    public DocBlockKind Kind   { get; init; }
    public IReadOnlyList<TextLine>? Lines { get; init; }
    public TableBlock?  Table  { get; init; }
    public ImageBlock?  Image  { get; init; }

    public static DocBlock FromLines(IEnumerable<TextLine> lines)
        => new() { Kind = DocBlockKind.TextLines, Lines = lines.ToList() };
    public static DocBlock FromTable(TableBlock t)
        => new() { Kind = DocBlockKind.Table, Table = t };
    public static DocBlock FromImage(ImageBlock img)
        => new() { Kind = DocBlockKind.Image, Image = img };
}

/// <summary>One page of extracted structured content from the PDF.</summary>
public sealed record DocPage
{
    public IReadOnlyList<DocBlock> Blocks  { get; init; }
    public double WidthPt  { get; init; }
    public double HeightPt { get; init; }

    // Backward-compat: wrap a flat TextLine list in a single TextLines block.
    public DocPage(IReadOnlyList<TextLine> lines, double widthPt, double heightPt)
    {
        Blocks   = new[] { DocBlock.FromLines(lines) };
        WidthPt  = widthPt;
        HeightPt = heightPt;
    }

    public DocPage(IReadOnlyList<DocBlock> blocks, double widthPt, double heightPt)
    {
        Blocks   = blocks;
        WidthPt  = widthPt;
        HeightPt = heightPt;
    }
}

/// <summary>
/// Builds a high-fidelity .docx from structured page data.
/// Handles paragraphs, headings, bullet/numbered lists, tables, and embedded images.
/// </summary>
public static class DocxBuilder
{
    public static void Build(IReadOnlyList<DocPage> pages, string outputPath)
    {
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart  = doc.AddMainDocumentPart();
        AddStylesPart(mainPart);
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        double median = ComputeMedianSize(pages);
        int    imgSeq = 1;

        for (int pi = 0; pi < pages.Count; pi++)
        {
            if (pi > 0) body.AppendChild(PageBreakParagraph());

            foreach (var block in pages[pi].Blocks)
            {
                switch (block.Kind)
                {
                    case DocBlockKind.TextLines:
                        foreach (var line in block.Lines!)
                            body.AppendChild(BuildParagraph(line, median));
                        break;
                    case DocBlockKind.Table:
                        body.AppendChild(BuildTable(block.Table!));
                        break;
                    case DocBlockKind.Image:
                        var imgPara = TryBuildImageParagraph(block.Image!, mainPart, imgSeq++);
                        if (imgPara != null) body.AppendChild(imgPara);
                        break;
                }
            }
        }

        if (pages.Count > 0)
            body.AppendChild(BuildSectionProperties(pages[0]));

        if (!body.Elements<Paragraph>().Any())
            body.AppendChild(new Paragraph());

        mainPart.Document.Save();
    }

    // ── Paragraph ─────────────────────────────────────────────────────────────

    private static Paragraph BuildParagraph(TextLine line, double medianSizePt)
    {
        var para = new Paragraph();
        var pPr  = BuildParagraphProperties(line, medianSizePt);
        if (pPr != null) para.AppendChild(pPr);

        foreach (var run in line.Runs)
        {
            if (string.IsNullOrEmpty(run.Text)) continue;
            var w   = new Run();
            var rPr = BuildRunProperties(run);
            if (rPr.HasChildren) w.AppendChild(rPr);
            w.AppendChild(new Text(run.Text) { Space = SpaceProcessingModeValues.Preserve });
            para.AppendChild(w);
        }
        return para;
    }

    private static ParagraphProperties? BuildParagraphProperties(TextLine line, double median)
    {
        // Explicit semantic type from structure analysis takes priority over size heuristic
        if (line.Type is LineType.Heading1 or LineType.Heading2 or LineType.Heading3)
        {
            var id = line.Type switch
            {
                LineType.Heading1 => "Heading1",
                LineType.Heading2 => "Heading2",
                _                 => "Heading3",
            };
            return new ParagraphProperties(new ParagraphStyleId { Val = id });
        }

        if (line.Type == LineType.BulletItem)
        {
            int left = 540 + line.IndentLevel * 360;
            return new ParagraphProperties(
                new ParagraphStyleId { Val = "ListBullet" },
                new Indentation { Left = left.ToString(), Hanging = "360" });
        }

        if (line.Type == LineType.NumberedItem)
        {
            int left = 540 + line.IndentLevel * 360;
            return new ParagraphProperties(
                new ParagraphStyleId { Val = "ListNumber" },
                new Indentation { Left = left.ToString(), Hanging = "360" });
        }

        // Auto-detect heading from font size when no explicit type is set
        if (median > 0 && line.MaxSizePt > median * 1.4 && line.MaxSizePt > 8)
        {
            var id = line.MaxSizePt > median * 2.0 ? "Heading1"
                   : line.MaxSizePt > median * 1.6 ? "Heading2"
                   : "Heading3";
            return new ParagraphProperties(new ParagraphStyleId { Val = id });
        }

        return null;
    }

    // ── Table ─────────────────────────────────────────────────────────────────

    private static Table BuildTable(TableBlock tbl)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableStyle { Val = "TableGrid" },
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder                { Val = BorderValues.Single, Size = 4, Space = 0 },
                new BottomBorder             { Val = BorderValues.Single, Size = 4, Space = 0 },
                new LeftBorder               { Val = BorderValues.Single, Size = 4, Space = 0 },
                new RightBorder              { Val = BorderValues.Single, Size = 4, Space = 0 },
                new InsideHorizontalBorder   { Val = BorderValues.Single, Size = 4, Space = 0 },
                new InsideVerticalBorder     { Val = BorderValues.Single, Size = 4, Space = 0 })));

        bool isHeader = true;
        foreach (var tRow in tbl.Rows)
        {
            var row = new DocumentFormat.OpenXml.Wordprocessing.TableRow();
            if (isHeader)
                row.AppendChild(new TableRowProperties(new TableHeader()));

            foreach (var cellRuns in tRow.Cells)
            {
                var cell = new TableCell();
                var cPr  = new TableCellProperties(
                    new TableCellWidth { Type = TableWidthUnitValues.Auto });
                if (isHeader)
                    cPr.AppendChild(new Shading
                    {
                        Val   = ShadingPatternValues.Clear,
                        Color = "auto",
                        Fill  = "D9D9D9",
                    });
                cell.AppendChild(cPr);

                var para = new Paragraph();
                foreach (var run in cellRuns)
                {
                    if (string.IsNullOrEmpty(run.Text)) continue;
                    var w   = new Run();
                    var rPr = BuildRunProperties(run);
                    if (isHeader && !rPr.Elements<Bold>().Any())
                        rPr.PrependChild(new Bold());
                    if (rPr.HasChildren) w.AppendChild(rPr);
                    w.AppendChild(new Text(run.Text) { Space = SpaceProcessingModeValues.Preserve });
                    para.AppendChild(w);
                }
                cell.AppendChild(para);
                row.AppendChild(cell);
            }

            table.AppendChild(row);
            isHeader = false;
        }

        return table;
    }

    // ── Image ─────────────────────────────────────────────────────────────────

    private static Paragraph? TryBuildImageParagraph(
        ImageBlock img, MainDocumentPart mainPart, int seq)
    {
        try
        {
            var partType = img.MediaType == "image/jpeg" ? ImagePartType.Jpeg : ImagePartType.Png;
            var imgPart  = mainPart.AddImagePart(partType);
            using var ms = new MemoryStream(img.Data);
            imgPart.FeedData(ms);
            string relId = mainPart.GetIdOfPart(imgPart);

            // Max usable width: page width minus margins (720 twips each side = 1440 twips = ~2cm)
            // Clamp image so it fits within a Letter/A4 body width ≈ 450pt
            double maxW   = 450.0;
            double scale  = img.WidthPt > maxW ? maxW / img.WidthPt : 1.0;
            long   cx     = (long)(img.WidthPt  * scale * 12700); // EMU
            long   cy     = (long)(img.HeightPt * scale * 12700);

            var drawing = new Drawing(
                new WP.Inline(
                    new WP.Extent        { Cx = cx, Cy = cy },
                    new WP.EffectExtent  { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new WP.DocProperties { Id = (uint)seq, Name = $"Img{seq}" },
                    new WP.NonVisualGraphicFrameDrawingProperties(
                        new A.GraphicFrameLocks { NoChangeAspect = true }),
                    new A.Graphic(
                        new A.GraphicData(
                            new Pic.Picture(
                                new Pic.NonVisualPictureProperties(
                                    new Pic.NonVisualDrawingProperties { Id = 0U, Name = $"Img{seq}" },
                                    new Pic.NonVisualPictureDrawingProperties()),
                                new Pic.BlipFill(
                                    new A.Blip { Embed = relId },
                                    new A.Stretch(new A.FillRectangle())),
                                new Pic.ShapeProperties(
                                    new A.Transform2D(
                                        new A.Offset  { X = 0L, Y = 0L },
                                        new A.Extents { Cx = cx, Cy = cy }),
                                    new A.PresetGeometry(new A.AdjustValueList())
                                        { Preset = A.ShapeTypeValues.Rectangle })))
                        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })));

            var para = new Paragraph(new Run(drawing));
            para.PrependChild(new ParagraphProperties(
                new Justification { Val = JustificationValues.Center }));
            return para;
        }
        catch
        {
            return null;
        }
    }

    // ── Run properties ────────────────────────────────────────────────────────

    private static RunProperties BuildRunProperties(TextRun run)
    {
        var rPr = new RunProperties();
        if (!string.IsNullOrEmpty(run.FontFamily))
            rPr.AppendChild(new RunFonts { Ascii = run.FontFamily, HighAnsi = run.FontFamily });
        if (run.SizePt >= 4)
            rPr.AppendChild(new FontSize { Val = ((int)(run.SizePt * 2)).ToString() });
        if (run.Bold)   rPr.AppendChild(new Bold());
        if (run.Italic) rPr.AppendChild(new Italic());
        if (!string.IsNullOrEmpty(run.ColorHex) && run.ColorHex != "000000")
            rPr.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Color { Val = run.ColorHex });
        return rPr;
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static Paragraph PageBreakParagraph() =>
        new(new Run(new Break { Type = BreakValues.Page }));

    private static SectionProperties BuildSectionProperties(DocPage page)
    {
        uint w = (uint)Math.Max(1, page.WidthPt  * 20);
        uint h = (uint)Math.Max(1, page.HeightPt * 20);
        return new SectionProperties(
            new PageSize   { Width = w, Height = h },
            new PageMargin { Top = 720, Bottom = 720, Left = 720, Right = 720 });
    }

    private static double ComputeMedianSize(IReadOnlyList<DocPage> pages)
    {
        var sizes = pages
            .SelectMany(p => p.Blocks)
            .Where(b => b.Kind == DocBlockKind.TextLines)
            .SelectMany(b => b.Lines!)
            .SelectMany(l => l.Runs)
            .Select(r => r.SizePt)
            .Where(s => s >= 4)
            .OrderBy(s => s)
            .ToList();
        if (sizes.Count == 0) return 12;
        return sizes[(sizes.Count - 1) / 2];
    }

    // ── Styles part ───────────────────────────────────────────────────────────

    private static void AddStylesPart(MainDocumentPart mainPart)
    {
        var part   = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        var normal = new Style { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true };
        normal.AppendChild(new StyleName { Val = "Normal" });
        normal.AppendChild(new PrimaryStyle());
        styles.AppendChild(normal);

        (string id, string name, int pt)[] headings =
        [
            ("Heading1", "heading 1", 24),
            ("Heading2", "heading 2", 18),
            ("Heading3", "heading 3", 14),
        ];
        foreach (var (id, name, pt) in headings)
        {
            var style = new Style { Type = StyleValues.Paragraph, StyleId = id };
            style.AppendChild(new StyleName { Val = name });
            style.AppendChild(new BasedOn { Val = "Normal" });
            style.AppendChild(new PrimaryStyle());
            style.AppendChild(new NextParagraphStyle { Val = "Normal" });
            var sRpr = new StyleRunProperties();
            sRpr.AppendChild(new Bold());
            sRpr.AppendChild(new FontSize { Val = (pt * 2).ToString() });
            style.AppendChild(sRpr);
            styles.AppendChild(style);
        }

        AddListStyle(styles, "ListBullet", "List Bullet");
        AddListStyle(styles, "ListNumber", "List Number");

        part.Styles = styles;
        part.Styles.Save();
    }

    private static void AddListStyle(Styles styles, string id, string name)
    {
        var style = new Style { Type = StyleValues.Paragraph, StyleId = id };
        style.AppendChild(new StyleName { Val = name });
        style.AppendChild(new BasedOn { Val = "Normal" });
        style.AppendChild(new PrimaryStyle());
        style.AppendChild(new StyleParagraphProperties(
            new Indentation { Left = "720", Hanging = "360" }));
        styles.AppendChild(style);
    }
}
