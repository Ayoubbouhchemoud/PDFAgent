using System.Runtime.InteropServices;
using PDFAgent.Core.Models;

namespace PDFAgent.PdfEngine;

/// <summary>
/// P/Invoke wrapper around the PDFium text-page API.
/// PDFium is already loaded and initialised by PdfiumViewer; we must not call
/// FPDF_InitLibrary again, and we serialise every call through a shared lock.
/// </summary>
internal static class PdfiumTextNative
{
    private static readonly object _lock = PdfiumSharedLock.Instance;

    // ── PDFium P/Invoke ───────────────────────────────────────────────────────

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr FPDF_LoadDocument(
        [MarshalAs(UnmanagedType.LPStr)] string filePath,
        [MarshalAs(UnmanagedType.LPStr)] string? password);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void FPDF_CloseDocument(IntPtr document);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void FPDF_ClosePage(IntPtr page);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern float FPDF_GetPageWidthF(IntPtr page);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern float FPDF_GetPageHeightF(IntPtr page);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr FPDFText_LoadPage(IntPtr page);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void FPDFText_ClosePage(IntPtr textPage);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FPDFText_CountChars(IntPtr textPage);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint FPDFText_GetUnicode(IntPtr textPage, int index);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FPDFText_GetCharBox(
        IntPtr textPage, int index,
        out double left, out double right,
        out double bottom, out double top);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern double FPDFText_GetFontSize(IntPtr textPage, int index);

    // FPDFText_GetFontInfo: returns required buffer length (including NUL), or 0 on failure.
    // flags receives PDF font descriptor bits: bit 6 (0x40) = Italic, bit 18 (0x40000) = ForceBold.
    // This export did not exist in very old PDFium builds; we guard with _fontInfoAvailable.
    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint FPDFText_GetFontInfo(
        IntPtr textPage, int index,
        [Out] byte[]? buffer, uint bufLen,
        out int flags);

    private static volatile bool _fontInfoChecked;
    private static volatile bool _fontInfoAvailable;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts word-level segments (with bounding boxes in PdfSharp point coords,
    /// i.e. top-left origin, Y increases downward) for one 0-based page.
    /// </summary>
    public static IReadOnlyList<PdfTextSegment> ExtractWords(
        string filePath, int pageIndex, string? password = null)
    {
        lock (_lock)
        {
            var doc = FPDF_LoadDocument(filePath, password);
            if (doc == IntPtr.Zero) return Array.Empty<PdfTextSegment>();

            try
            {
                var page = FPDF_LoadPage(doc, pageIndex);
                if (page == IntPtr.Zero) return Array.Empty<PdfTextSegment>();

                try
                {
                    var pageHeightPts = FPDF_GetPageHeightF(page);
                    var textPage      = FPDFText_LoadPage(page);
                    if (textPage == IntPtr.Zero) return Array.Empty<PdfTextSegment>();

                    try
                    {
                        return GroupIntoWords(textPage, pageIndex + 1, pageHeightPts);
                    }
                    finally { FPDFText_ClosePage(textPage); }
                }
                finally { FPDF_ClosePage(page); }
            }
            finally { FPDF_CloseDocument(doc); }
        }
    }

    // ── Grouping ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<PdfTextSegment> GroupIntoWords(
        IntPtr textPage, int pageNumber, float pageHeightPts)
    {
        var count = FPDFText_CountChars(textPage);
        if (count <= 0) return Array.Empty<PdfTextSegment>();

        // Gather all characters with their boxes and font index in a single pass
        var chars = new List<CharInfo>(count);
        for (var i = 0; i < count; i++)
        {
            var code = FPDFText_GetUnicode(textPage, i);
            if (code == 0) continue;

            var c = code < 0x10000 ? (char)code : '?';
            if (!FPDFText_GetCharBox(textPage, i, out var left, out var right, out var bottom, out var top))
                continue;

            // Skip zero-size boxes (invisible/space glyphs that have no real extent)
            if (right - left < 0.01 && top - bottom < 0.01 && char.IsWhiteSpace(c))
            {
                chars.Add(new CharInfo(c, left, right, bottom, top, 0, i));
                continue;
            }

            chars.Add(new CharInfo(c, left, right, bottom, top,
                FPDFText_GetFontSize(textPage, i), i));
        }

        // Group consecutive non-whitespace characters into words
        var words = new List<PdfTextSegment>();
        int segIdx = 0, start = 0;

        while (start < chars.Count)
        {
            // Skip whitespace / line-break characters
            while (start < chars.Count && char.IsWhiteSpace(chars[start].Ch))
                start++;
            if (start >= chars.Count) break;

            // Collect the word
            var end = start;
            while (end < chars.Count && !char.IsWhiteSpace(chars[end].Ch))
                end++;

            var span = chars.Skip(start).Take(end - start).ToList();
            var text = new string(span.Select(ci => ci.Ch).ToArray());

            // Union bounding box in PDFium coordinates (bottom-left origin, Y up)
            var minLeft   = span.Min(ci => ci.Left);
            var maxRight  = span.Max(ci => ci.Right);
            var minBottom = span.Min(ci => ci.Bottom);
            var maxTop    = span.Max(ci => ci.Top);
            var avgFont   = span.Where(ci => ci.FontSize > 0).Select(ci => ci.FontSize)
                               .DefaultIfEmpty(12).Average();

            // Convert to PdfSharp coordinates: top-left origin, Y down
            var pdfX      = minLeft;
            var pdfY      = pageHeightPts - maxTop;   // "from top" = pageH - pdfium_top
            var pdfWidth  = Math.Max(maxRight - minLeft, 1);
            var pdfHeight = Math.Max(maxTop - minBottom, 1);

            // Extract font info from the first non-whitespace char of the word
            TryGetFontInfo(textPage, span[0].CharIndex, out var fontName, out var isBold, out var isItalic);

            words.Add(new PdfTextSegment
            {
                Text         = text,
                X            = pdfX,
                Y            = pdfY,
                Width        = pdfWidth,
                Height       = pdfHeight,
                FontSize     = avgFont,
                FontName     = fontName,
                IsBold       = isBold,
                IsItalic     = isItalic,
                PageNumber   = pageNumber,
                SegmentIndex = segIdx++,
            });

            start = end;
        }

        return words;
    }

    private static bool TryGetFontInfo(IntPtr textPage, int charIndex,
        out string? fontName, out bool isBold, out bool isItalic)
    {
        fontName = null; isBold = false; isItalic = false;

        if (_fontInfoChecked && !_fontInfoAvailable)
            return false;

        try
        {
            var needed = FPDFText_GetFontInfo(textPage, charIndex, null, 0, out var flags);
            _fontInfoChecked = true;
            _fontInfoAvailable = true;

            if (needed > 1) // needed includes NUL terminator
            {
                var buf = new byte[needed];
                FPDFText_GetFontInfo(textPage, charIndex, buf, needed, out flags);
                fontName = System.Text.Encoding.UTF8.GetString(buf, 0, (int)needed - 1);
            }

            // PDF font descriptor flags (PDF spec): bit 7 (0x40) = Italic, bit 19 (0x40000) = ForceBold
            isBold   = (flags & 0x40000) != 0;
            isItalic = (flags & 0x40) != 0;

            // Also detect from font name as a safety net
            if (fontName != null)
            {
                var n = fontName.ToLowerInvariant();
                if (!isBold)   isBold   = n.Contains("bold");
                if (!isItalic) isItalic = n.Contains("italic") || n.Contains("oblique");
            }

            return true;
        }
        catch (EntryPointNotFoundException)
        {
            _fontInfoChecked  = true;
            _fontInfoAvailable = false;
            return false;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct CharInfo(
        char Ch, double Left, double Right, double Bottom, double Top, double FontSize, int CharIndex);
}
