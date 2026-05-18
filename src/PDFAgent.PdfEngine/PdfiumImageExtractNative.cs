using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PDFAgent.Core.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PDFAgent.PdfEngine;

/// <summary>
/// Extracts embedded images from a PDF using two complementary passes:
///
/// Pass 1 — PDFium page-object API (primary):
///   Iterates all FPDF_PAGEOBJ_IMAGE objects on each page.
///   • JPEG (DCTDecode)     → raw stream bytes saved as .jpg (zero re-encoding)
///   • JPEG 2000 (JPXDecode)→ raw stream bytes saved as .jp2/.j2k (zero re-encoding)
///   • All other encodings  → FPDFImageObj_GetBitmap → lossless .png
///   Inline images are included because PDFium surfaces them as image objects.
///
/// Pass 2 — PdfPig content-stream scan (supplemental):
///   Walks each page's content stream including nested Form XObjects.
///   Images that were already saved in Pass 1 (identified by MD5) are skipped.
///   • JPEG raw bytes → .jpg
///   • JPEG 2000 raw bytes → .jp2/.j2k
///   • TryGetPng() → .png (handles FlateDecode, LZW, etc.)
///   Failures on individual images are logged and skipped; the scan continues.
///
/// The original PDF is never modified. Images are saved to <outputFolder>.
/// Same XObjects referenced on multiple pages are deduplicated via MD5.
/// </summary>
internal static class PdfiumImageExtractNative
{
    private static readonly object _lock = PdfiumSharedLock.Instance;

    // ── PDFium page-object type constants ─────────────────────────────────────
    private const int FPDF_PAGEOBJ_IMAGE = 3;   // NOT 1 — that is TEXT
    // private const int FPDF_PAGEOBJ_FORM  = 5; // reserved for future recursion

    // ── PDFium bitmap format constants ────────────────────────────────────────
    private const int FPDFBitmap_Gray = 1;
    private const int FPDFBitmap_BGR  = 2;
    private const int FPDFBitmap_BGRx = 3;
    private const int FPDFBitmap_BGRA = 4;

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr FPDF_LoadMemDocument(
        byte[] data, int size,
        [MarshalAs(UnmanagedType.LPStr)] string? password);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void FPDF_CloseDocument(IntPtr doc);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FPDF_GetPageCount(IntPtr doc);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr FPDF_LoadPage(IntPtr doc, int pageIndex);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void FPDF_ClosePage(IntPtr page);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FPDFPage_CountObjects(IntPtr page);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr FPDFPage_GetObject(IntPtr page, int objIndex);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FPDFPageObj_GetType(IntPtr pageObj);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FPDFImageObj_GetImageMetadata(
        IntPtr imageObj, IntPtr page, ref FpdfImageObjMetadata metadata);

    // Two overloads of the same native function: first to query needed buffer size,
    // second to fill the buffer.
    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl,
               EntryPoint = "FPDFImageObj_GetImageDataRaw")]
    private static extern uint FPDFImageObj_GetRawSize(
        IntPtr imageObj, IntPtr doc, IntPtr zeroBuf, uint zeroLen);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl,
               EntryPoint = "FPDFImageObj_GetImageDataRaw")]
    private static extern uint FPDFImageObj_GetRawData(
        IntPtr imageObj, IntPtr doc, byte[] buffer, uint bufLen);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr FPDFImageObj_GetBitmap(IntPtr imageObj);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FPDFBitmap_GetWidth(IntPtr bitmap);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FPDFBitmap_GetHeight(IntPtr bitmap);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FPDFBitmap_GetStride(IntPtr bitmap);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FPDFBitmap_GetFormat(IntPtr bitmap);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void FPDFBitmap_Destroy(IntPtr bitmap);

    // ── Metadata struct ───────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct FpdfImageObjMetadata
    {
        public uint  Width;
        public uint  Height;
        public float HorizontalDpi;
        public float VerticalDpi;
        public uint  BitsPerPixel;
        public int   Colorspace;
        public uint  MarkedContentId;
    }

    // ── Public result ─────────────────────────────────────────────────────────

    public sealed record ExtractionResult(int Saved, int Skipped, int ScannedPages);

    // ── Entry point ───────────────────────────────────────────────────────────

    public static ExtractionResult ExtractImages(
        string inputPath,
        string? password,
        ExtractImagesOptions options,
        string outputFolder,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(outputFolder);

        var pdfBase      = Path.GetFileNameWithoutExtension(inputPath);
        var seen         = new HashSet<string>(StringComparer.Ordinal); // global MD5 dedup
        var saved        = 0;
        var skipped      = 0;
        var scannedPages = 0;

        // ── Pass 1: PDFium direct image-object enumeration ────────────────────
        // This covers all FPDF_PAGEOBJ_IMAGE objects including inline images.
        lock (_lock)
        {
            var bytes = File.ReadAllBytes(inputPath);
            var doc   = FPDF_LoadMemDocument(bytes, bytes.Length, password);
            if (doc == IntPtr.Zero)
                throw new InvalidOperationException("PDFium: cannot open the document.");
            try
            {
                int pageCount = FPDF_GetPageCount(doc);
                var pages     = ResolvePageNumbers(options, pageCount).ToList();

                for (int pi = 0; pi < pages.Count; pi++)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(0.5 * pi / pages.Count); // first half of progress

                    int pageNum = pages[pi];
                    var page    = FPDF_LoadPage(doc, pageNum - 1);
                    if (page == IntPtr.Zero) { skipped++; continue; }

                    try
                    {
                        int objCount = FPDFPage_CountObjects(page);
                        var imgObjs  = new List<IntPtr>(objCount);

                        for (int oi = 0; oi < objCount; oi++)
                        {
                            var obj = FPDFPage_GetObject(page, oi);
                            if (obj != IntPtr.Zero && FPDFPageObj_GetType(obj) == FPDF_PAGEOBJ_IMAGE)
                                imgObjs.Add(obj);
                        }

                        if (imgObjs.Count == 1)
                        {
                            var m = new FpdfImageObjMetadata();
                            FPDFImageObj_GetImageMetadata(imgObjs[0], page, ref m);
                            if ((long)m.Width * m.Height > 1_000_000)
                                scannedPages++;
                        }

                        int imgIdx = 0;
                        foreach (var imgObj in imgObjs)
                        {
                            var meta = new FpdfImageObjMetadata();
                            FPDFImageObj_GetImageMetadata(imgObj, page, ref meta);

                            if (meta.Width  < (uint)options.MinDimensionPx ||
                                meta.Height < (uint)options.MinDimensionPx)
                            { skipped++; continue; }

                            var (fileBytes, ext) = GetImageBytesViaPdfium(imgObj, doc);
                            if (fileBytes == null || fileBytes.Length == 0)
                            { skipped++; continue; }

                            var hash = Md5Hex(fileBytes);
                            if (!seen.Add(hash)) continue;

                            imgIdx++;
                            var name = $"{pdfBase}_p{pageNum:D3}_{imgIdx:D2}_{meta.Width}x{meta.Height}.{ext}";
                            File.WriteAllBytes(Path.Combine(outputFolder, name), fileBytes);
                            saved++;
                        }
                    }
                    finally { FPDF_ClosePage(page); }
                }
            }
            finally { FPDF_CloseDocument(doc); }
        }

        // ── Pass 2: PdfPig content-stream scan (Form XObjects + any gaps) ─────
        // PdfPig recurses into Do operators that reference Form XObjects, finding
        // images that PDFium's top-level object list does not surface.
        try
        {
            using var pigDoc  = UglyToad.PdfPig.PdfDocument.Open(inputPath);
            int pageCount     = pigDoc.NumberOfPages;
            var pages         = ResolvePageNumbers(options, pageCount).ToList();

            for (int pi = 0; pi < pages.Count; pi++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(0.5 + 0.5 * pi / pages.Count);

                int pageNum = pages[pi];
                IReadOnlyList<IPdfImage> pigImages;
                try   { pigImages = pigDoc.GetPage(pageNum).GetImages().ToList(); }
                catch { continue; } // PdfPig can't parse this page — skip

                int imgIdx = 0;
                foreach (var img in pigImages)
                {
                    try
                    {
                        if (img.IsImageMask) continue;
                        if (img.WidthInSamples  < options.MinDimensionPx ||
                            img.HeightInSamples < options.MinDimensionPx)
                        { skipped++; continue; }

                        var raw = img.RawBytes.Count == 0 ? Array.Empty<byte>() : img.RawBytes.ToArray();

                        byte[]? fileBytes = null;
                        string  ext       = "png";

                        if (raw.Length > 0 && IsJpeg(raw))
                        {
                            fileBytes = raw;
                            ext       = "jpg";
                        }
                        else if (raw.Length > 0 && IsJpeg2000(raw))
                        {
                            fileBytes = raw;
                            ext       = IsJp2Container(raw) ? "jp2" : "j2k";
                        }
                        else if (img.TryGetPng(out var png) && png is { Length: > 0 })
                        {
                            fileBytes = png;
                            ext       = "png";
                        }

                        if (fileBytes == null || fileBytes.Length == 0)
                        { skipped++; continue; }

                        var hash = Md5Hex(fileBytes);
                        if (!seen.Add(hash)) continue; // already written in Pass 1

                        imgIdx++;
                        var name = $"{pdfBase}_p{pageNum:D3}_f{imgIdx:D2}_{img.WidthInSamples}x{img.HeightInSamples}.{ext}";
                        File.WriteAllBytes(Path.Combine(outputFolder, name), fileBytes);
                        saved++;
                    }
                    catch { skipped++; } // per-image failure — continue the scan
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* PdfPig can't open the document — Pass 1 results stand */ }

        progress?.Report(1.0);
        return new ExtractionResult(saved, skipped, scannedPages);
    }

    // ── PDFium extraction helpers ─────────────────────────────────────────────

    private static (byte[]? Bytes, string Ext) GetImageBytesViaPdfium(IntPtr imgObj, IntPtr doc)
    {
        // Try raw compressed stream first
        uint rawSize = FPDFImageObj_GetRawSize(imgObj, doc, IntPtr.Zero, 0);
        if (rawSize > 0)
        {
            var raw = new byte[rawSize];
            FPDFImageObj_GetRawData(imgObj, doc, raw, rawSize);

            if (IsJpeg(raw))     return (raw, "jpg");
            if (IsJpeg2000(raw)) return (raw, IsJp2Container(raw) ? "jp2" : "j2k");
        }

        // Fallback: let PDFium decode to a BGRA/BGR/Gray bitmap → lossless PNG
        var png = BitmapToPng(imgObj);
        return (png, "png");
    }

    private static byte[]? BitmapToPng(IntPtr imgObj)
    {
        var bmp = FPDFImageObj_GetBitmap(imgObj);
        if (bmp == IntPtr.Zero) return null;
        try
        {
            int w      = FPDFBitmap_GetWidth(bmp);
            int h      = FPDFBitmap_GetHeight(bmp);
            int stride = FPDFBitmap_GetStride(bmp);
            int fmt    = FPDFBitmap_GetFormat(bmp);
            var ptr    = FPDFBitmap_GetBuffer(bmp);

            if (ptr == IntPtr.Zero || w <= 0 || h <= 0) return null;

            var pixels = new byte[stride * h];
            Marshal.Copy(ptr, pixels, 0, pixels.Length);

            return fmt switch
            {
                FPDFBitmap_BGRA => PixelsToPng(pixels, w, h, stride,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb),
                FPDFBitmap_BGRx => PixelsToPng(pixels, w, h, stride,
                    System.Drawing.Imaging.PixelFormat.Format32bppRgb),
                FPDFBitmap_BGR  => PixelsToPng(pixels, w, h, stride,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb),
                FPDFBitmap_Gray => GrayToPng(pixels, w, h, stride),
                _               => null,
            };
        }
        finally { FPDFBitmap_Destroy(bmp); }
    }

    private static byte[] PixelsToPng(
        byte[] pixels, int w, int h, int srcStride,
        System.Drawing.Imaging.PixelFormat pf)
    {
        using var drawBmp = new System.Drawing.Bitmap(w, h, pf);
        var bmpData = drawBmp.LockBits(
            new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, pf);
        try
        {
            int rowBytes = Math.Min(srcStride, bmpData.Stride);
            for (int y = 0; y < h; y++)
                Marshal.Copy(pixels, y * srcStride,
                    bmpData.Scan0 + y * bmpData.Stride, rowBytes);
        }
        finally { drawBmp.UnlockBits(bmpData); }

        using var ms = new MemoryStream();
        drawBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] GrayToPng(byte[] gray, int w, int h, int srcStride)
    {
        using var drawBmp = new System.Drawing.Bitmap(w, h,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var bmpData = drawBmp.LockBits(
            new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[w * 3];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    byte v = gray[y * srcStride + x];
                    row[x * 3] = row[x * 3 + 1] = row[x * 3 + 2] = v;
                }
                Marshal.Copy(row, 0, bmpData.Scan0 + y * bmpData.Stride, w * 3);
            }
        }
        finally { drawBmp.UnlockBits(bmpData); }

        using var ms = new MemoryStream();
        drawBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    // ── Format detection (magic bytes) ────────────────────────────────────────

    private static bool IsJpeg(byte[] d) =>
        d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF;

    private static bool IsJp2Container(byte[] d) =>
        d.Length > 7 &&
        d[0] == 0x00 && d[1] == 0x00 && d[2] == 0x00 && d[3] == 0x0C &&
        d[4] == 0x6A && d[5] == 0x50 && d[6] == 0x20 && d[7] == 0x20;

    private static bool IsJpeg2000(byte[] d) =>
        IsJp2Container(d) ||
        (d.Length > 3 && d[0] == 0xFF && d[1] == 0x4F && d[2] == 0xFF && d[3] == 0x51);

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static string Md5Hex(byte[] data)
    {
        using var md5 = MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(data));
    }

    private static IEnumerable<int> ResolvePageNumbers(ExtractImagesOptions opts, int pageCount) =>
        opts.Scope switch
        {
            ImageExtractScope.CurrentPage => [Math.Clamp(opts.CurrentPageNumber, 1, pageCount)],
            ImageExtractScope.PageRange   => ParsePageRange(opts.PageRangeText, pageCount),
            _                             => Enumerable.Range(1, pageCount),
        };

    private static IEnumerable<int> ParsePageRange(string text, int maxPage)
    {
        var result = new SortedSet<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = part.IndexOf('-');
            if (dash > 0 &&
                int.TryParse(part[..dash], out var from) &&
                int.TryParse(part[(dash + 1)..], out var to))
            {
                for (var i = Math.Max(1, from); i <= Math.Min(maxPage, to); i++)
                    result.Add(i);
            }
            else if (int.TryParse(part, out var single) && single >= 1 && single <= maxPage)
                result.Add(single);
        }
        return result.Count > 0 ? result : Enumerable.Range(1, maxPage);
    }
}
