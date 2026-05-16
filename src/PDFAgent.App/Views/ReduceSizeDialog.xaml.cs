using System.Windows;
using System.Windows.Controls;

namespace PDFAgent.App.Views;

public partial class ReduceSizeDialog : Window
{
    /// <summary>null = lossless mode; value = image DPI chosen by user.</summary>
    public int? SelectedImageDpi  { get; private set; }
    public int  SelectedJpegQuality { get; private set; } = 80;

    public ReduceSizeDialog(long currentFileSizeBytes)
    {
        InitializeComponent();
        FileSizeLabel.Text = $"Current size: {FormatBytes(currentFileSizeBytes)}";
        UpdateHints();
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (ImageOptions == null) return;
        var imageMode = RadioImage.IsChecked == true;
        ImageOptions.IsEnabled = imageMode;
        ImageOptions.Opacity   = imageMode ? 1.0 : 0.4;
    }

    private void DpiSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DpiLabel == null) return;
        var dpi = (int)DpiSlider.Value;
        DpiLabel.Text = $"{dpi} DPI";
        UpdateHints();
    }

    private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityLabel == null) return;
        var q = (int)QualitySlider.Value;
        QualityLabel.Text = $"{q}%";
        UpdateHints();
    }

    private void UpdateHints()
    {
        if (DpiHint == null || QualityHint == null) return;

        var dpi = (int)DpiSlider.Value;
        DpiHint.Text = dpi switch
        {
            <= 72  => "Screen quality — very small file, noticeable pixelation on print.",
            <= 96  => "Web / screen quality — compact file, acceptable for on-screen reading.",
            <= 150 => "Standard quality — good balance of size and clarity.",
            <= 200 => "High quality — suitable for printing.",
            _      => "Print quality — near-lossless visually, larger file.",
        };

        var q = (int)QualitySlider.Value;
        QualityHint.Text = q switch
        {
            <= 30  => "Low quality — artifacts may be visible on images.",
            <= 60  => "Medium quality — acceptable for most documents.",
            <= 80  => "Good quality — minimal visible compression artifacts.",
            <= 90  => "High quality — virtually indistinguishable from original.",
            _      => "Maximum quality — very close to lossless.",
        };
    }

    private void Compress_Click(object sender, RoutedEventArgs e)
    {
        SelectedImageDpi    = RadioImage.IsChecked == true ? (int)DpiSlider.Value : null;
        SelectedJpegQuality = (int)QualitySlider.Value;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:N1} MB";
        if (bytes >= 1_024)     return $"{bytes / 1_024.0:N0} KB";
        return $"{bytes} B";
    }
}
