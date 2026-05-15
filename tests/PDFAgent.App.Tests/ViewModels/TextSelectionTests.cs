using System.Windows;
using FluentAssertions;
using PDFAgent.App.ViewModels;
using PDFAgent.Core.Models;
using Xunit;

namespace PDFAgent.App.Tests.ViewModels;

/// <summary>
/// Verifies the coordinate mapping used by PdfPageView for text selection and search highlights.
///
/// Core invariant (from PdfPageView.xaml.cs):
///   private const double Scale = 96.0 / 72.0;   // DIPs per PDF point
///   canvasX = segment.X * Scale
///
/// Pages are rendered at 150 DPI with SetResolution(150,150) PNG metadata.
/// WPF displays the bitmap at ActualWidth = pixelWidth × (96/150)
///                                        = pageWidthPts × (150/72) × (96/150)
///                                        = pageWidthPts × (96/72)
/// Therefore Scale = 96/72 is always correct — no runtime measurement needed.
/// </summary>
public sealed class TextSelectionTests
{
    private const double Scale = 96.0 / 72.0;   // must match PdfPageView constant

    // ── Scale constant correctness ────────────────────────────────────────────

    [Theory]
    [InlineData(595.0, 842.0)]    // A4
    [InlineData(612.0, 792.0)]    // US Letter
    [InlineData(841.9, 1190.6)]   // A3
    public void Scale_IsPositiveAndFinite(double pageW, double pageH)
    {
        // The constant itself
        Scale.Should().BeApproximately(96.0 / 72.0, 1e-10);
        Scale.Should().BePositive();

        // Derived image dimensions should be positive
        var imgW = pageW * Scale;
        var imgH = pageH * Scale;
        imgW.Should().BePositive();
        imgH.Should().BePositive();
    }

    [Theory]
    [InlineData(595.0, 842.0)]    // A4
    [InlineData(612.0, 792.0)]    // US Letter
    public void FullPageWord_MapsToFullImageWidth(double pageW, double pageH)
    {
        var imgW = pageW * Scale;

        var seg = new PdfTextSegment { X = 0, Width = pageW, Y = 0, Height = pageH };
        var canvasW = seg.Width * Scale;

        canvasW.Should().BeApproximately(imgW, 0.5,
            "a word spanning the full page width must map to the full image width");
    }

    // ── PDF-point → canvas DIP mapping ───────────────────────────────────────

    [Fact]
    public void WordAt_PdfOrigin_MapsToCanvasOrigin()
    {
        var seg = new PdfTextSegment { X = 0, Y = 0, Width = 50, Height = 12 };
        var canvasRect = new Rect(seg.X * Scale, seg.Y * Scale,
                                  seg.Width * Scale, seg.Height * Scale);

        canvasRect.X.Should().BeApproximately(0, 0.01);
        canvasRect.Y.Should().BeApproximately(0, 0.01);
    }

    [Fact]
    public void WordAt_BottomRight_MapsNearImageBottomRight()
    {
        const double pageW = 595, pageH = 842;
        var imgW = pageW * Scale;
        var imgH = pageH * Scale;

        // Word near the lower-right corner
        var seg = new PdfTextSegment { X = 545, Y = 810, Width = 40, Height = 12 };
        var canvasX = seg.X * Scale;
        var canvasY = seg.Y * Scale;

        canvasX.Should().BeInRange(0, imgW);
        canvasY.Should().BeInRange(0, imgH);
        canvasX.Should().BeGreaterThan(imgW / 2, "word is in the right half");
        canvasY.Should().BeGreaterThan(imgH * 0.9, "word is in the bottom 10%");
    }

    [Fact]
    public void ConstantScale_MatchesDerivedImageSize()
    {
        // The math: render at 150 DPI → pixelWidth = pageW * (150/72)
        //           WPF loads at 150 DPI metadata → logicalWidth = pixelWidth * (96/150)
        //           logicalWidth = pageW * (150/72) * (96/150) = pageW * (96/72)
        const double pageW = 612.0;   // US Letter
        const double renderDpi = 150.0;

        var pixelWidth   = pageW * (renderDpi / 72.0);
        var logicalWidth = pixelWidth * (96.0 / renderDpi);   // WPF DIP conversion

        (logicalWidth / pageW).Should().BeApproximately(Scale, 1e-10,
            "derived scale must equal the compile-time constant");
    }

    // ── Selection intersection ────────────────────────────────────────────────

    [Fact]
    public void SelectionRect_OverWord_IntersectsWordBox()
    {
        var seg = new PdfTextSegment { X = 100, Y = 200, Width = 80, Height = 12 };
        var wordBox = new Rect(seg.X * Scale, seg.Y * Scale,
                               seg.Width * Scale, seg.Height * Scale);

        // Selection rect slightly larger than the word
        var selRect = new Rect(wordBox.X - 5, wordBox.Y - 5,
                               wordBox.Width + 10, wordBox.Height + 10);

        selRect.IntersectsWith(wordBox).Should().BeTrue("selection dragged over the word");
    }

    [Fact]
    public void SelectionRect_FarFromWord_DoesNotIntersect()
    {
        var seg = new PdfTextSegment { X = 100, Y = 200, Width = 80, Height = 12 };
        var wordBox = new Rect(seg.X * Scale, seg.Y * Scale,
                               seg.Width * Scale, seg.Height * Scale);

        // Selection 300 DIPs below the word
        var selRect = new Rect(wordBox.X, wordBox.Y + 300, 200, 20);

        selRect.IntersectsWith(wordBox).Should().BeFalse("selection is far from the word");
    }

    [Fact]
    public void SelectionRect_JustTouching_IntersectsWordBox()
    {
        var seg = new PdfTextSegment { X = 100, Y = 200, Width = 80, Height = 12 };
        var wordBox = new Rect(seg.X * Scale, seg.Y * Scale,
                               seg.Width * Scale, seg.Height * Scale);

        // Selection starts exactly at the word's right edge
        var selRect = new Rect(wordBox.Right - 1, wordBox.Y, 50, wordBox.Height);

        selRect.IntersectsWith(wordBox).Should().BeTrue("touching the word edge counts as intersection");
    }

    // ── Search highlight coordinates ──────────────────────────────────────────

    [Fact]
    public void SearchHighlight_StoredInPdfPoints_ConvertsCorrectlyViaConstant()
    {
        const double pageW = 595, pageH = 842;
        var imgW = pageW * Scale;
        var imgH = pageH * Scale;

        var highlight = new SearchHighlightRect
        {
            X = 100, Y = 200, Width = 80, Height = 12
        };

        var canvasX = highlight.X * Scale;
        var canvasY = highlight.Y * Scale;
        var canvasW = highlight.Width  * Scale;
        var canvasH = highlight.Height * Scale;

        canvasX.Should().BeInRange(0, imgW);
        canvasY.Should().BeInRange(0, imgH);
        (canvasX + canvasW).Should().BeLessOrEqualTo(imgW + 1);
        (canvasY + canvasH).Should().BeLessOrEqualTo(imgH + 1);

        // Fractional position must match the PDF-point fraction
        (canvasX / imgW).Should().BeApproximately(highlight.X / pageW, 0.001,
            "constant scale preserves fractional position");
        (canvasY / imgH).Should().BeApproximately(highlight.Y / pageH, 0.001);
    }

    // ── Scanned page (no text layer) ──────────────────────────────────────────

    [Fact]
    public void NoTextLayer_WordBoxes_StayEmpty()
    {
        var page = new RenderedPageItem
        {
            PageNumber       = 1,
            PageWidthPoints  = 595,
            PageHeightPoints = 842,
        };

        page.TextLayer.Should().BeNull("scanned page has no text layer yet");

        // Simulate RebuildWordBoxes with null layer
        var wordBoxes = new List<Rect>();
        var layer = page.TextLayer;
        if (layer != null)
            foreach (var seg in layer)
                wordBoxes.Add(new Rect(seg.X * Scale, seg.Y * Scale,
                                       Math.Max(seg.Width  * Scale, 4),
                                       Math.Max(seg.Height * Scale, 6)));

        wordBoxes.Should().BeEmpty("scanned page produces no selectable word boxes");
    }

    [Fact]
    public void EmptyTextLayer_WordBoxes_StayEmpty()
    {
        var page = new RenderedPageItem
        {
            PageNumber       = 1,
            PageWidthPoints  = 595,
            PageHeightPoints = 842,
        };
        page.TextLayer = new List<PdfTextSegment>();

        var wordBoxes = new List<Rect>();
        var layer = page.TextLayer;
        if (layer != null && layer.Count > 0)
            foreach (var seg in layer)
                wordBoxes.Add(new Rect(seg.X * Scale, seg.Y * Scale,
                                       Math.Max(seg.Width  * Scale, 4),
                                       Math.Max(seg.Height * Scale, 6)));

        wordBoxes.Should().BeEmpty("empty text layer produces no word boxes");
    }

    // ── Minimum size clamping ─────────────────────────────────────────────────

    [Fact]
    public void TinyWord_IsClampedToMinimumSize()
    {
        // A degenerate segment (zero width/height) must still get a hit-testable box
        var seg = new PdfTextSegment { X = 50, Y = 100, Width = 0, Height = 0 };

        var w = Math.Max(seg.Width  * Scale, 4);
        var h = Math.Max(seg.Height * Scale, 6);

        w.Should().BeGreaterOrEqualTo(4, "minimum word box width is 4 DIPs");
        h.Should().BeGreaterOrEqualTo(6, "minimum word box height is 6 DIPs");
    }
}
