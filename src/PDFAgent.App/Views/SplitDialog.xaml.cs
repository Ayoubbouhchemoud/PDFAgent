using System.Windows;
using PDFAgent.Core.Interfaces;

namespace PDFAgent.App.Views;

public partial class SplitDialog : Window
{
    public SplitMode SelectedMode { get; private set; } = SplitMode.SplitAll;
    public string PageRange { get; private set; } = string.Empty;
    public int EveryN { get; private set; } = 2;

    public SplitDialog()
    {
        InitializeComponent();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (PageRangeBox == null) return;
        PageRangeBox.IsEnabled = RadioExtract.IsChecked == true;
        EveryNBox.IsEnabled    = RadioEveryN.IsChecked  == true;
        ValidationMsg.Visibility = Visibility.Collapsed;
    }

    private void Split_Click(object sender, RoutedEventArgs e)
    {
        ValidationMsg.Visibility = Visibility.Collapsed;

        if (RadioSplitAll.IsChecked == true)
        {
            SelectedMode = SplitMode.SplitAll;
        }
        else if (RadioExtract.IsChecked == true)
        {
            SelectedMode = SplitMode.SplitRange;
            PageRange    = PageRangeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(PageRange))
            {
                ShowValidation("Please enter a page range (e.g. 1, 3-5, 7-10).");
                PageRangeBox.Focus();
                return;
            }
        }
        else if (RadioEveryN.IsChecked == true)
        {
            SelectedMode = SplitMode.SplitEvery;
            if (!int.TryParse(EveryNBox.Text.Trim(), out var n) || n < 1)
            {
                ShowValidation("Please enter a valid number of pages (1 or more).");
                EveryNBox.Focus();
                return;
            }
            EveryN = n;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowValidation(string msg)
    {
        ValidationMsg.Text       = msg;
        ValidationMsg.Visibility = Visibility.Visible;
    }
}
