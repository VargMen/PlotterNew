using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace PlotterNew.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        [ObservableProperty]
        private double _currPanX = 0.0;

        [ObservableProperty]
        private double _currPanY = 0.0;

        [ObservableProperty]
        private double _elapsedTime = 0.0;

        [ObservableProperty]
        private ObservableCollection<int> _waveformIds = new ObservableCollection<int> { 0, 1, 2 };

        [ObservableProperty]
        private int _currWaveformId = 0;

        [ObservableProperty]
        private double _currWaveformScale = 1.0;

        [ObservableProperty]
        private double _currWaveformOffset = 0.0;
    }
}
