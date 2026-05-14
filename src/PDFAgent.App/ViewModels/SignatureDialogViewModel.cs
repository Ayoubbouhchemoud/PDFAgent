using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PDFAgent.App.ViewModels;

public sealed partial class SignatureDialogViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUploadTab))]
    private bool _isDrawTab = true;

    public bool IsUploadTab
    {
        get => !IsDrawTab;
        set => IsDrawTab = !value;
    }

    [ObservableProperty] private double        _threshold     = 230;
    [ObservableProperty] private BitmapSource? _uploadedPreview;
    [ObservableProperty] private string        _uploadStatus  = "No image selected";

    // Set by code-behind on Apply
    public byte[]?       SignatureBytes    { get; set; }
    public BitmapSource? RawUploadedBitmap { get; set; }
}
