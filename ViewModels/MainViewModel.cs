using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PlotterNew.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ObservableCollection<double> _sliderCentersY = new ObservableCollection<double> { 0, 75, 150, 225, 300, 375, 450, 525, 600, 675 };
        [ObservableProperty]
        private ObservableCollection<double> _sliderValues = new ObservableCollection<double> { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };

        [ObservableProperty]
        private double _currPanX = 0.0;

        [ObservableProperty]
        private double _currPanY = 0.0;

        [ObservableProperty]
        private double _elapsedTime = 0.0;

        [ObservableProperty]
        private ObservableCollection<int> _waveformIds = new ObservableCollection<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        [ObservableProperty]
        private ObservableCollection<double> _waveformScales = new ObservableCollection<double> { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };

        [ObservableProperty]
        private ObservableCollection<double> _waveformOffsets = new ObservableCollection<double> { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        [ObservableProperty]
        private int _currWaveformId = 0;

        [ObservableProperty]
        private double _currWaveformScale = 1.0;

        [ObservableProperty]
        private double _currWaveformOffset = 0.0;
    }
}
