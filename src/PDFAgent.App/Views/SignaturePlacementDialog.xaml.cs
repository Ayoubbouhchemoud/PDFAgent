using System.Windows;
using PDFAgent.Core.Models;

namespace PDFAgent.App.Views;

public partial class SignaturePlacementDialog : Window
{
    public int ChosenPage { get; private set; } = 1;
    public SignaturePlacement ChosenPlacement { get; private set; } = SignaturePlacement.BottomRight;

    public SignaturePlacementDialog(int totalPages, int currentPage)
    {
        InitializeComponent();
        PageNumberBox.Text   = Math.Clamp(currentPage, 1, totalPages).ToString();
        TotalPagesLabel.Text = $"of {totalPages}";
    }

    private void Place_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PageNumberBox.Text.Trim(), out var page) || page < 1)
        {
            MessageBox.Show("Enter a valid page number.", "Place Signature",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ChosenPage = page;
        ChosenPlacement = RadioBottomLeft.IsChecked   == true ? SignaturePlacement.BottomLeft
                        : RadioBottomCenter.IsChecked == true ? SignaturePlacement.BottomCenter
                        : SignaturePlacement.BottomRight;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
