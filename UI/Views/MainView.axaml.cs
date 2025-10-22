using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using PlotterNew.UI.ViewModels;
using System;
using System.Reflection;

namespace PlotterNew
{
    public partial class MainView : Window
    {
        Core.Services.FakeSineFeed _dataFeed = new Core.Services.FakeSineFeed(seriesCount: 1, sampleRateHz: 500.0, batchSize: 16);

        public MainView()
        {
            InitializeComponent();

            this.Focusable = true;
            this.KeyDown += OnKeyDownHandler;

            _dataFeed.BatchReady += OnDataBatchReady;
        }

        public void OnDataBatchReady(Core.Interfaces.DataBatch batch)
        {
            App.MainVM.TotalSamplesAmount += batch.X.Count;
            System.Diagnostics.Debug.WriteLine($"Batch received: {batch.X.Count} samples.");
        }

        private void OnKeyDownHandler(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();

            if (e.Key == Key.Space)
            {
                if (!App.MainVM.IsDataFeedRunning)
                {
                    _dataFeed.Start();
                    App.MainVM.IsDataFeedRunning = true;
                }
                else
                {
                    _dataFeed.Stop();
                    App.MainVM.IsDataFeedRunning = false;
                }
            }
        }
    }
}