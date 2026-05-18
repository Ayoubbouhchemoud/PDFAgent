using System.Windows;
using PDFAgent.Core.Models;

namespace PDFAgent.App.Views;

public partial class ConvertFromPdfDialog : Window
{
    private readonly int _totalPages;

    public ExportFormat SelectedFormat =>
        DocxRadio.IsChecked == true ? ExportFormat.Docx :
        XlsxRadio.IsChecked == true ? ExportFormat.Xlsx :
        MdRadio.IsChecked   == true ? ExportFormat.Md   :
        TxtRadio.IsChecked  == true ? ExportFormat.Txt  :
        PngRadio.IsChecked  == true ? ExportFormat.Png  :
        JpgRadio.IsChecked  == true ? ExportFormat.Jpg  :
        SvgRadio.IsChecked  == true ? ExportFormat.Svg  :
        ExportFormat.Html;

    public ConvertFromPdfDialog(string fileName, int totalPages, int currentPage)
    {
        InitializeComponent();
        _totalPages = totalPages;
        var multi = totalPages > 1;
        InfoLabel.Text = $"{fileName}  ·  {totalPages} page{(multi ? "s" : "")}";

        if (multi)
            MultiPageHint.Text =
                $"Image formats: all {totalPages} pages will be saved in a single ZIP archive.";

        PngRadio.Checked  += (_, _) => UpdateMultiPageHint();
        JpgRadio.Checked  += (_, _) => UpdateMultiPageHint();
        SvgRadio.Checked  += (_, _) => UpdateMultiPageHint();
        HtmlRadio.Checked += (_, _) => UpdateMultiPageHint();
        DocxRadio.Checked += (_, _) => UpdateMultiPageHint();
        XlsxRadio.Checked += (_, _) => UpdateMultiPageHint();
        MdRadio.Checked   += (_, _) => UpdateMultiPageHint();
        TxtRadio.Checked  += (_, _) => UpdateMultiPageHint();
    }

    private void UpdateMultiPageHint()
    {
        bool isImg = PngRadio.IsChecked == true ||
                     JpgRadio.IsChecked == true ||
                     SvgRadio.IsChecked == true;
        MultiPageHint.Text = (_totalPages > 1 && isImg)
            ? $"Image formats: all {_totalPages} pages will be saved in a single ZIP archive."
            : string.Empty;
    }

    private void Export_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
