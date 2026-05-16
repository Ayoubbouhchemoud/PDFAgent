using System.Windows;

namespace PDFAgent.App.Views;

public enum ConvertFromFormat { Images, Word, Text }

public partial class ConvertFromPdfDialog : Window
{
    public ConvertFromFormat SelectedFormat    { get; private set; }
    public bool              UseJpeg          { get; private set; }
    public int               SelectedDpi      { get; private set; } = 150;
    public bool              AllPages         { get; private set; } = true;

    public ConvertFromPdfDialog(string fileName, int totalPages, int currentPage)
    {
        InitializeComponent();
        var singlePage = totalPages == 1;
        InfoLabel.Text = $"File: {fileName}  ·  {totalPages} page{(singlePage ? "" : "s")}  ·  current page: {currentPage}";
        if (singlePage) RadioCurrentPage.IsEnabled = false;
        UpdateDpiHint();
    }

    private void Format_Changed(object sender, RoutedEventArgs e)
    {
        if (ImageOptions == null) return;
        var isImages = RadioImages.IsChecked == true;
        ImageOptions.IsEnabled = isImages;
        ImageOptions.Opacity   = isImages ? 1.0 : 0.4;
    }

    private void DpiSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DpiLabel == null) return;
        DpiLabel.Text = $"{(int)DpiSlider.Value} DPI";
        UpdateDpiHint();
    }

    private void UpdateDpiHint()
    {
        if (DpiHint == null) return;
        DpiHint.Text = (int)DpiSlider.Value switch
        {
            <= 96  => "Screen quality — compact file, suitable for on-screen viewing.",
            <= 150 => "Standard quality — good balance of size and clarity.",
            <= 200 => "High quality — suitable for printing.",
            _      => "Print quality — near-lossless, larger file.",
        };
    }

    private void Convert_Click(object sender, RoutedEventArgs e)
    {
        SelectedFormat = RadioImages.IsChecked == true ? ConvertFromFormat.Images
                       : RadioWord.IsChecked   == true ? ConvertFromFormat.Word
                       :                                 ConvertFromFormat.Text;
        UseJpeg   = RadioJpeg.IsChecked == true;
        SelectedDpi = (int)DpiSlider.Value;
        AllPages  = RadioAllPages.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
