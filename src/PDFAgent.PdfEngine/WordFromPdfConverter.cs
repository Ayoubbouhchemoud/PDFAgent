using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PDFAgent.PdfEngine;

/// <summary>
/// Converts a PDF to a Word document (.docx) using Word's native PDF import
/// (available in Microsoft Word 2013 and later).
/// </summary>
internal static class WordFromPdfConverter
{
    public static string? ConvertToDocx(string pdfPath)
    {
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"pdfagent_docx_{Guid.NewGuid():N}.docx");

        string? result = null;
        var t = new Thread(() =>
        {
            dynamic? word = null;
            dynamic? doc  = null;
            try
            {
                var type = Type.GetTypeFromProgID("Word.Application");
                if (type == null) return;

                word = Activator.CreateInstance(type)!;
                word.Visible       = false;
                word.DisplayAlerts = 0; // wdAlertsNone

                // Word opens PDFs by converting them — this is native in Word 2013+
                doc = word.Documents.Open(
                    pdfPath,
                    ConfirmConversions: false,
                    ReadOnly:           false,
                    AddToRecentFiles:   false);

                // wdFormatDocumentDefault (16) → .docx
                doc.SaveAs2(outputPath, 16);

                if (File.Exists(outputPath))
                    result = outputPath;
            }
            catch { }
            finally
            {
                try { doc?.Close(0); }   catch { } // 0 = wdDoNotSaveChanges
                try { word?.Quit(0); }   catch { }
                try { if (doc  != null) Marshal.ReleaseComObject(doc);  } catch { }
                try { if (word != null) Marshal.ReleaseComObject(word); } catch { }
            }
        });

        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(TimeSpan.FromSeconds(120));

        return result;
    }
}
