using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Jabsco.Ui.ViewModels;

public partial class ReplayViewModel : ViewModelBase
{
    [ObservableProperty] private int _currentStep;
    [ObservableProperty] private int _totalSteps;
    [ObservableProperty] private byte[]? _currentFrame;
    [ObservableProperty] private bool _isLoaded;

    public ObservableCollection<EventItemViewModel> Events { get; } = [];
}
