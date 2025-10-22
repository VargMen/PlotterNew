using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PlotterNew.Core.Interfaces;
using PlotterNew.Core.Models;
using PlotterNew.Core.Services.Math;
using PlotterNew.UI.Interactions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace PlotterNew.UI.ViewModels
{
    public partial class PlotViewModel : ObservableObject
    {
        [ObservableProperty] private bool _isDataFeedRunning = false;
        [ObservableProperty] private PlotModel _model = new();
       
        private readonly ConcurrentQueue<DataBatch> _pending = new();
        private readonly DispatcherTimer _uiTimer;

        public PlotViewModel(IDataFeed feed)
        {
            feed.BatchReady += OnBatch;
            feed.Start();

            _uiTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(10),
            DispatcherPriority.Render,
            (_, _) => OnUiTick());
        }

        private void OnBatch(DataBatch batch) => _pending.Enqueue(batch);

        private void OnUiTick()
        {
            while (_pending.TryDequeue(out var item))
            {
                AppendBatch(item);
            }
        }

        private void AppendBatch(DataBatch b)
        {
            // grow series slots if needed
            while (Model.SeriesList.Count < b.Y.Count)
                Model.SeriesList.Add(new Series());

            Model.TimeSeries.AddRange(b.X);
      
            for (int s = 0; s < b.Y.Count; s++)
                Model.SeriesList[s].YCoords.AddRange(b.Y[s]);
        }
    }
}
