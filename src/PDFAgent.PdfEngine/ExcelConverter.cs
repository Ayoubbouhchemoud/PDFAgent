using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PDFAgent.PdfEngine;

internal static class ExcelConverter
{
    public static string? ConvertToPdf(string excelPath)
    {
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"pdfagent_excel_{Guid.NewGuid():N}.pdf");

        string? result = null;
        var t = new Thread(() =>
        {
            dynamic? excel = null;
            dynamic? wb    = null;
            try
            {
                var type = Type.GetTypeFromProgID("Excel.Application");
                if (type == null) return;

                excel = Activator.CreateInstance(type)!;
                excel.Visible        = false;
                excel.DisplayAlerts  = false;
                excel.ScreenUpdating = false;

                wb = excel.Workbooks.Open(
                    excelPath,
                    UpdateLinks:       false,
                    ReadOnly:          true,
                    AddToMru:          false);

                // xlTypePDF = 0
                wb.ExportAsFixedFormat(0, outputPath);

                if (File.Exists(outputPath))
                    result = outputPath;
            }
            catch { }
            finally
            {
                try { wb?.Close(false); }    catch { }
                try { excel?.Quit(); }       catch { }
                try { if (wb    != null) Marshal.ReleaseComObject(wb);    } catch { }
                try { if (excel != null) Marshal.ReleaseComObject(excel); } catch { }
            }
        });

        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(TimeSpan.FromSeconds(120));

        return result;
    }
}
