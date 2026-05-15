namespace PDFAgent.PdfEngine;

/// <summary>Single lock object shared by all PDFium P/Invoke call sites.</summary>
internal static class PdfiumSharedLock
{
    internal static readonly object Instance = new();
}
