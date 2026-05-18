using System.Windows;
using System.Windows.Controls;
using PDFAgent.App.Services;
using PDFAgent.Core.Interfaces;

namespace PDFAgent.App.Views;

public partial class ExtractImagesDialog : Window
{
    private readonly int _totalPages;

    public ImageExtractScope Scope { get; private set; } = ImageExtractScope.AllPages;
    public string PageRangeText   { get; private set; } = "";
    public int    MinDimensionPx  { get; private set; } = 32;

    public ExtractImagesDialog(int currentPage, int totalPages)
    {
        InitializeComponent();
        _totalPages = totalPages;
        CurrentPageLabel.Text = $"Current page (page {currentPage})";
    }

    private void Scope_Checked(object sender, RoutedEventArgs e)
    {
        if (PageRangeBox == null) return;
        PageRangeBox.IsEnabled = RadioPageRange.IsChecked == true;
    }

    private void Extract_Click(object sender, RoutedEventArgs e)
    {
        ValidationMsg.Visibility = Visibility.Collapsed;

        if (RadioPageRange.IsChecked == true)
        {
            var range = PageRangeBox.Text.Trim();
            if (string.IsNullOrEmpty(range))
            {
                ValidationMsg.Text = "Please enter a page range (e.g. 1, 3-5, 7-10).";
                ValidationMsg.Visibility = Visibility.Visible;
                return;
            }
            PageRangeText = range;
            Scope = ImageExtractScope.PageRange;
        }
        else if (RadioCurrentPage.IsChecked == true)
        {
            Scope = ImageExtractScope.CurrentPage;
        }
        else
        {
            Scope = ImageExtractScope.AllPages;
        }

        if (!int.TryParse(MinSizeBox.Text.Trim(), out var minSize) || minSize < 1)
        {
            ValidationMsg.Text = "Minimum size must be a positive number.";
            ValidationMsg.Visibility = Visibility.Visible;
            return;
        }
        MinDimensionPx = minSize;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
