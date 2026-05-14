using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PDFAgent.App.ViewModels;

public sealed partial class TextAnnotationViewModel : ObservableObject
{
    [ObservableProperty] private double _x        = 60;
    [ObservableProperty] private double _y        = 60;
    [ObservableProperty] private double _width    = 220;
    [ObservableProperty] private double _height   = 60;
    [ObservableProperty] private string _text     = "";
    [ObservableProperty] private double _fontSize = 12;

    public event EventHandler? DeleteRequested;

    [RelayCommand] private void Delete() => DeleteRequested?.Invoke(this, EventArgs.Empty);
}
