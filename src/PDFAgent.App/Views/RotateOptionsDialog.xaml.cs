using System.Windows;

namespace PDFAgent.App.Views;

public partial class RotateOptionsDialog : Window
{
    public RotatePageScope PageScope { get; private set; } = RotatePageScope.All;
    public string PageRangeText { get; private set; } = string.Empty;
    public int Degrees { get; private set; } = 90;

    public int CurrentPageNumber
    {
        set => RadioCurrentPage.Content = $"Current page only (page {value})";
    }

    public RotateOptionsDialog()
    {
        InitializeComponent();
    }

    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        PageScope = RadioAllPages.IsChecked == true ? RotatePageScope.All
            : RadioCurrentPage.IsChecked == true ? RotatePageScope.CurrentPage
            : RotatePageScope.Range;

        PageRangeText = PageRangeBox.Text.Trim();

        Degrees = Radio90.IsChecked == true ? 90
            : Radio180.IsChecked == true ? 180
            : 270;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public enum RotatePageScope { All, CurrentPage, Range }
