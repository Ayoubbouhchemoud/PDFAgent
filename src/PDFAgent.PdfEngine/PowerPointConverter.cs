using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PDFAgent.PdfEngine;

internal static class PowerPointConverter
{
    public static string? ConvertToPdf(string pptPath)
    {
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"pdfagent_ppt_{Guid.NewGuid():N}.pdf");

        string? result = null;
        var t = new Thread(() =>
        {
            dynamic? ppt  = null;
            dynamic? pres = null;
            try
            {
                var type = Type.GetTypeFromProgID("PowerPoint.Application");
                if (type == null) return;

                ppt = Activator.CreateInstance(type)!;
                ppt.Visible = false;

                // WithWindow: false avoids flickering; ReadOnly: true
                pres = ppt.Presentations.Open(
                    pptPath,
                    ReadOnly:   true,
                    Untitled:   true,
                    WithWindow: false);

                // ppSaveAsPDF = 32
                pres.SaveAs(outputPath, 32);

                if (File.Exists(outputPath))
                    result = outputPath;
            }
            catch { }
            finally
            {
                try { pres?.Close(); }          catch { }
                try { ppt?.Quit(); }            catch { }
                try { if (pres != null) Marshal.ReleaseComObject(pres); } catch { }
                try { if (ppt  != null) Marshal.ReleaseComObject(ppt);  } catch { }
            }
        });

        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(TimeSpan.FromSeconds(120));

        return result;
    }
}
