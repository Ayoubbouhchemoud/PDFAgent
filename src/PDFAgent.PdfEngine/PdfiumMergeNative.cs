using System.IO;
using System.Runtime.InteropServices;

namespace PDFAgent.PdfEngine;

/// <summary>
/// Merges PDF files using PDFium's own FPDF_ImportPages + FPDF_SaveAsCopy,
/// bypassing PdfSharp entirely.  PDFium handles all PDF versions and
/// cross-reference stream formats that PdfSharp 6.x cannot import.
/// </summary>
internal static class PdfiumMergeNative
{
    // Reuse the same serialisation lock as PdfiumTextNative.
    private static readonly object _lock = PdfiumSharedLock.Instance;

    // ── P/Invoke declarations ─────────────────────────────────────────────────

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr FPDF_CreateNewDocument();

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr FPDF_LoadMemDocument(
        byte[] dataBuffer, int size,
        [MarshalAs(UnmanagedType.LPStr)] string? password);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void FPDF_CloseDocument(IntPtr document);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FPDF_GetPageCount(IntPtr document);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool FPDF_ImportPages(
        IntPtr destDoc,
        IntPtr srcDoc,
        [MarshalAs(UnmanagedType.LPStr)] string? pageRange,
        int insertIndex);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool FPDF_SaveAsCopy(
        IntPtr document,
        ref FpdfFileWrite fileWrite,
        uint flags);

    // ── FPDF_FILEWRITE callback structure ─────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct FpdfFileWrite
    {
        public int    Version;     // must be 1
        public IntPtr WriteBlock;  // function pointer
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WriteBlockDelegate(IntPtr pFileWrite, IntPtr pData, uint size);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Merges <paramref name="inputPaths"/> in order into <paramref name="outputPath"/>.
    /// Throws on failure.
    /// </summary>
    public static void MergeFiles(IReadOnlyList<string> inputPaths, string outputPath)
    {
        lock (_lock)
        {
            var destDoc = IntPtr.Zero;
            var srcDocs = new List<IntPtr>();

            try
            {
                destDoc = FPDF_CreateNewDocument();
                if (destDoc == IntPtr.Zero)
                    throw new InvalidOperationException("PDFium: failed to create destination document.");

                var insertIndex = 0;

                foreach (var path in inputPaths)
                {
                    // Load from bytes to avoid any path-encoding limitations.
                    var bytes  = File.ReadAllBytes(path);
                    var srcDoc = FPDF_LoadMemDocument(bytes, bytes.Length, null);
                    if (srcDoc == IntPtr.Zero)
                        throw new InvalidOperationException(
                            $"PDFium: could not open '{System.IO.Path.GetFileName(path)}'.");

                    srcDocs.Add(srcDoc);

                    var count = FPDF_GetPageCount(srcDoc);
                    if (!FPDF_ImportPages(destDoc, srcDoc, null, insertIndex))
                        throw new InvalidOperationException(
                            $"PDFium: failed to import pages from '{System.IO.Path.GetFileName(path)}'.");

                    insertIndex += count;
                }

                // Stream the output through the FPDF_FILEWRITE callback.
                using var ms = new MemoryStream();

                WriteBlockDelegate writeDelegate = (_, pData, size) =>
                {
                    var buf = new byte[size];
                    Marshal.Copy(pData, buf, 0, (int)size);
                    ms.Write(buf, 0, (int)size);
                    return 1;
                };

                var fw = new FpdfFileWrite
                {
                    Version    = 1,
                    WriteBlock = Marshal.GetFunctionPointerForDelegate(writeDelegate),
                };

                if (!FPDF_SaveAsCopy(destDoc, ref fw, 0))
                    throw new InvalidOperationException("PDFium: FPDF_SaveAsCopy failed.");

                GC.KeepAlive(writeDelegate);

                File.WriteAllBytes(outputPath, ms.ToArray());
            }
            finally
            {
                foreach (var doc in srcDocs)
                    if (doc != IntPtr.Zero) FPDF_CloseDocument(doc);
                if (destDoc != IntPtr.Zero)
                    FPDF_CloseDocument(destDoc);
            }
        }
    }
}
