using System.Windows;
using PDFAgent.Core.Models;

namespace PDFAgent.App.Views;

public partial class ConvertFromPdfDialog : Window
{
    public ExportFormat SelectedFormat { get; private set; } = ExportFormat.Txt;
    public int          SelectedDpi    { get; private set; } = 150;
    public bool         AllPages       { get; private set; } = true;

    public ConvertFromPdfDialog(string fileName, int totalPages, int currentPage)
    {
        InitializeComponent();
        var multi = totalPages > 1;
        InfoLabel.Text = $"{fileName}  ·  {totalPages} page{(multi ? "s" : "")}  ·  current page: {currentPage}";
        if (!multi) RadioCurrentPage.IsEnabled = false;
    }

    private void Format_Changed(object sender, RoutedEventArgs e)
    {
        if (ImageOptionsPanel == null) return;
        var showImages = RadioPng.IsChecked == true || RadioJpg.IsChecked == true || RadioSvg.IsChecked == true;
        ImageOptionsPanel.Visibility = showImages ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DpiSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DpiLabel == null) return;
        DpiLabel.Text = $"{(int)DpiSlider.Value} DPI";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        SelectedFormat = RadioTxt.IsChecked    == true ? ExportFormat.Txt
                       : RadioHtml.IsChecked   == true ? ExportFormat.Html
                       : RadioEpub.IsChecked   == true ? ExportFormat.Epub
                       : RadioPng.IsChecked    == true ? ExportFormat.Png
                       : RadioJpg.IsChecked    == true ? ExportFormat.Jpg
                       : RadioSvg.IsChecked    == true ? ExportFormat.Svg
                       : RadioPdf.IsChecked    == true ? ExportFormat.Pdf
                       : RadioPdfA1.IsChecked  == true ? ExportFormat.PdfA1b
                       : RadioPdfA2.IsChecked  == true ? ExportFormat.PdfA2b
                       : RadioPdfA3.IsChecked  == true ? ExportFormat.PdfA3b
                       : RadioSecure.IsChecked == true ? ExportFormat.SecurePdf
                       : RadioDocx.IsChecked   == true ? ExportFormat.Docx
                       : RadioPptx.IsChecked   == true ? ExportFormat.Pptx
                       :                                 ExportFormat.Xlsx;

        SelectedDpi = (int)(DpiSlider?.Value ?? 150);
        AllPages    = RadioAllPages?.IsChecked != false;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
