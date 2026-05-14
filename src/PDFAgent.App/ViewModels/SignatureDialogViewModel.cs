using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PDFAgent.Core.Models;

namespace PDFAgent.App.ViewModels;

public sealed partial class SignatureDialogViewModel : ObservableObject
{
    // Tab selection
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUploadTab))]
    private bool _isDrawTab = true;

    public bool IsUploadTab
    {
        get => !IsDrawTab;
        set => IsDrawTab = !value;
    }

    // Placement
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaceBottomLeft))]
    [NotifyPropertyChangedFor(nameof(PlaceBottomCenter))]
    [NotifyPropertyChangedFor(nameof(PlaceBottomRight))]
    private SignaturePlacement _placement = SignaturePlacement.BottomRight;

    public bool PlaceBottomLeft
    {
        get => Placement == SignaturePlacement.BottomLeft;
        set { if (value) Placement = SignaturePlacement.BottomLeft; }
    }
    public bool PlaceBottomCenter
    {
        get => Placement == SignaturePlacement.BottomCenter;
        set { if (value) Placement = SignaturePlacement.BottomCenter; }
    }
    public bool PlaceBottomRight
    {
        get => Placement == SignaturePlacement.BottomRight;
        set { if (value) Placement = SignaturePlacement.BottomRight; }
    }

    // Page
    [ObservableProperty] private int _targetPage = 1;
    [ObservableProperty] private int _totalPages  = 1;

    // Upload tab
    [ObservableProperty] private double       _threshold      = 230;
    [ObservableProperty] private BitmapSource? _uploadedPreview;
    [ObservableProperty] private string        _uploadStatus  = "No image selected";

    // Set by code-behind when Apply is clicked (draw) or upload is processed
    public byte[]?        SignatureBytes     { get; set; }
    public BitmapSource?  RawUploadedBitmap  { get; set; }
}
