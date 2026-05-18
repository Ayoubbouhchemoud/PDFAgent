using System.Windows;
using PDFAgent.Core.Models;

namespace PDFAgent.App.Views;

public partial class SecurePdfDialog : Window
{
    public ProtectOptions? Options { get; private set; }

    // Keep these for any code that still references them via the old contract.
    public string? UserPassword  => Options?.UserPassword;
    public string? OwnerPassword => Options?.OwnerPassword;

    public SecurePdfDialog() => InitializeComponent();

    private void Password_Changed(object sender, RoutedEventArgs e) =>
        ErrorLabel.Visibility = Visibility.Collapsed;

    private void AllowPrint_Changed(object sender, RoutedEventArgs e) =>
        AllowHiQPrintChk.IsEnabled = AllowPrintChk.IsChecked == true;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var user       = UserPwBox.Password;
        var userConf   = UserPwConfirmBox.Password;
        var owner      = OwnerPwBox.Password;
        var ownerConf  = OwnerPwConfirmBox.Password;

        // ── Validation ──────────────────────────────────────────────────────

        if (string.IsNullOrEmpty(user) && string.IsNullOrEmpty(owner))
        {
            ShowError("Enter at least one password.");
            return;
        }

        if (!string.IsNullOrEmpty(user) && user != userConf)
        {
            ShowError("User passwords do not match.");
            UserPwConfirmBox.Focus();
            return;
        }

        if (!string.IsNullOrEmpty(owner) && owner != ownerConf)
        {
            ShowError("Owner passwords do not match.");
            OwnerPwConfirmBox.Focus();
            return;
        }

        if (!string.IsNullOrEmpty(user) && user == owner)
        {
            ShowError("User and owner passwords must be different.");
            return;
        }

        // ── Collect options ─────────────────────────────────────────────────

        bool allowPrint = AllowPrintChk.IsChecked == true;

        Options = new ProtectOptions
        {
            UserPassword         = string.IsNullOrEmpty(user)  ? null : user,
            OwnerPassword        = string.IsNullOrEmpty(owner) ? null : owner,
            AllowPrint           = allowPrint,
            AllowHighQualityPrint = allowPrint && (AllowHiQPrintChk.IsChecked == true),
            AllowCopyText        = AllowCopyChk.IsChecked  == true,
            AllowModify          = AllowModifyChk.IsChecked == true,
            AllowFillForms       = AllowFillChk.IsChecked   == true,
            AllowAnnotations     = AllowAnnotChk.IsChecked  == true,
            Use256BitAes         = Aes256Radio.IsChecked    == true,
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string msg)
    {
        ErrorLabel.Text       = msg;
        ErrorLabel.Visibility = Visibility.Visible;
    }
}
