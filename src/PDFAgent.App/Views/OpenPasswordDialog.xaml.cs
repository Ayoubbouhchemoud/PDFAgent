using System.Windows;
using System.Windows.Input;

namespace PDFAgent.App.Views;

public partial class OpenPasswordDialog : Window
{
    public string Password { get; private set; } = string.Empty;

    public OpenPasswordDialog() => InitializeComponent();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PwBox.Password))
        {
            ErrorLabel.Text       = "Please enter the password.";
            ErrorLabel.Visibility = Visibility.Visible;
            return;
        }

        Password     = PwBox.Password;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void PwBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Open_Click(sender, new RoutedEventArgs());
    }
}
