using System.Windows;
using System.Windows.Input;

namespace PDFAgent.App.Views;

public partial class UnlockPdfDialog : Window
{
    public string Password { get; private set; } = string.Empty;

    public UnlockPdfDialog() => InitializeComponent();

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PwBox.Password))
        {
            ShowError("Please enter the PDF password.");
            return;
        }

        Password     = PwBox.Password;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void PwBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Remove_Click(sender, new RoutedEventArgs());
    }

    private void ShowError(string msg)
    {
        ErrorLabel.Text       = msg;
        ErrorLabel.Visibility = Visibility.Visible;
    }
}
