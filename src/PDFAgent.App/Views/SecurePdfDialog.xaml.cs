using System.Windows;

namespace PDFAgent.App.Views;

public partial class SecurePdfDialog : Window
{
    public string? UserPassword  { get; private set; }
    public string? OwnerPassword { get; private set; }

    public SecurePdfDialog() => InitializeComponent();

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var user  = UserPwBox.Password;
        var owner = OwnerPwBox.Password;

        if (string.IsNullOrEmpty(user) && string.IsNullOrEmpty(owner))
        {
            ErrorLabel.Text       = "Enter at least one password.";
            ErrorLabel.Visibility = Visibility.Visible;
            return;
        }

        UserPassword  = string.IsNullOrEmpty(user)  ? null : user;
        OwnerPassword = string.IsNullOrEmpty(owner) ? null : owner;
        DialogResult  = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
