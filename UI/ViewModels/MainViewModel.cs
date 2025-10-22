using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlotterNew.Core.Interfaces;
using PlotterNew.Core.Services;
using PlotterNew.UI.ViewModels;
using System;

namespace PlotterNew.UI.ViewModels
{
    public sealed partial class MainViewModel : ObservableObject, IDisposable
    {
        // The Plot VM consumed by MainView / PlotView
        [ObservableProperty]
        private PlotViewModel plotVM;

        // Keep a handle to the feed so you can start/stop/dispose it
        private readonly IDataFeed _feed;

        // Default ctor for the app: create a demo feed
        public MainViewModel()
            : this(new FakeSineFeed(seriesCount: 2, sampleRateHz: 1000, batchSize: 8))
        { }

        // DI-friendly ctor: pass any IDataFeed (serial, TCP, file replay, etc.)
        public MainViewModel(IDataFeed feed)
        {
            _feed = feed;
            plotVM = new PlotViewModel(_feed);
        }

        // Optional: expose start/stop commands bound to buttons in the UI
        [RelayCommand] private void Start() => _feed.Start();
        [RelayCommand] private void Stop() => _feed.Stop();

        public void Dispose()
        {
            try { _feed.Stop(); } catch { /* ignore */ }
            _feed.Dispose();
        }
    }
}
