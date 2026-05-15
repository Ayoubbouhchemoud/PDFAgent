using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using PDFAgent.App.Services;

namespace PDFAgent.App.Views;

public partial class AddPageDialog : Window, INotifyPropertyChanged
{
    // Standard page sizes in points (1 pt = 1/72 inch)
    private const double A4Width     = 595.28;
    private const double A4Height    = 841.89;
    private const double LetterWidth = 612.0;
    private const double LetterHeight = 792.0;

    private readonly double _currentWidthPts;
    private readonly double _currentHeightPts;

    public string BeforeCurrentLabel { get; }
    public string AfterCurrentLabel  { get; }
    public string SameAsCurrentLabel { get; }

    // Results
    public AddPagePosition SelectedPosition { get; private set; }
    public double          SelectedWidthPts  { get; private set; }
    public double          SelectedHeightPts { get; private set; }

    public AddPageDialog(int currentPage, int totalPages, double currentWidthPts, double currentHeightPts)
    {
        _currentWidthPts  = currentWidthPts;
        _currentHeightPts = currentHeightPts;

        BeforeCurrentLabel = $"Before page {currentPage}";
        AfterCurrentLabel  = $"After page {currentPage}  (default)";
        SameAsCurrentLabel = $"Same as current page  ({currentWidthPts:F0} × {currentHeightPts:F0} pt)";

        DataContext = this;
        InitializeComponent();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        SelectedPosition = (BeforeCurrentRb.IsChecked == true) ? AddPagePosition.BeforeCurrent
                         : (AtBeginningRb.IsChecked   == true) ? AddPagePosition.AtBeginning
                         : (AtEndRb.IsChecked          == true) ? AddPagePosition.AtEnd
                                                                 : AddPagePosition.AfterCurrent;

        if (A4Rb.IsChecked == true)
        {
            SelectedWidthPts  = A4Width;
            SelectedHeightPts = A4Height;
        }
        else if (LetterRb.IsChecked == true)
        {
            SelectedWidthPts  = LetterWidth;
            SelectedHeightPts = LetterHeight;
        }
        else
        {
            SelectedWidthPts  = _currentWidthPts;
            SelectedHeightPts = _currentHeightPts;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
