using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PDFAgent.App.ViewModels;

public sealed partial class StickerViewModel : ObservableObject
{
    [ObservableProperty] private double _x      = 120;
    [ObservableProperty] private double _y      = 120;
    [ObservableProperty] private double _width  = 220;
    [ObservableProperty] private double _height = 90;

    public byte[]            Bytes      { get; init; } = [];
    public RenderedPageItem? ParentPage { get; init; }

    public event EventHandler? CommitRequested;
    public event EventHandler? DeleteRequested;

    [RelayCommand] private void Commit() => CommitRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void Delete() => DeleteRequested?.Invoke(this, EventArgs.Empty);
}
