using System.Windows;
using PDFAgent.Core.Models;

namespace PDFAgent.App.Views;

public partial class ConvertFromPdfDialog : Window
{
    public ExportFormat SelectedFormat =>
        DocxRadio.IsChecked == true ? ExportFormat.Docx : ExportFormat.Html;

    public ConvertFromPdfDialog(string fileName, int totalPages, int currentPage)
    {
        InitializeComponent();
        var multi = totalPages > 1;
        InfoLabel.Text = $"{fileName}  ·  {totalPages} page{(multi ? "s" : "")}";
    }

    private void Export_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
