using CommunityToolkit.Mvvm.ComponentModel;

namespace PlotterNew.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        [ObservableProperty]
        private double _currPanX;

        [ObservableProperty]
        private double _currPanY;

        [ObservableProperty]
        private double _elapsedTime;
    }
}
