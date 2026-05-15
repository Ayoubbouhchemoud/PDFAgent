using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PDFAgent.PdfEngine;

internal static class WordConverter
{
    public static string? ConvertToPdf(string wordPath)
    {
        var outputPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"pdfagent_word_{Guid.NewGuid():N}.pdf");

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

                doc = word.Documents.Open(
                    wordPath,
                    ReadOnly:          true,
                    AddToRecentFiles:  false);

                doc.ExportAsFixedFormat(outputPath, 17); // 17 = wdExportFormatPDF

                if (File.Exists(outputPath))
                    result = outputPath;
            }
            catch { /* Word not installed or conversion failed — result stays null */ }
            finally
            {
                try { doc?.Close(0); }  catch { } // 0 = wdDoNotSaveChanges
                try { word?.Quit(0); }  catch { }
                try { if (word != null) Marshal.ReleaseComObject(word); } catch { }
            }
        });

        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(TimeSpan.FromSeconds(90));

        return result;
    }
}
