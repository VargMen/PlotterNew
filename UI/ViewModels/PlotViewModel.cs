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
    public partial class PlotViewModel : ObservableObject, IPlotInteractor
    {
        [ObservableProperty] private PlotModel model = new();
        [ObservableProperty] private int renderVersion;

        private readonly ConcurrentQueue<DataBatch> _pending = new();
        private readonly DispatcherTimer _uiTimer;

        // tuning knobs
        //private const int TargetFps = 60;
        //private const double MaxFrameMs = 2.0;
        //private const int MaxBatchesPerTick = 2;
        //private const int MaxVisiblePtsPerSeries = 3000;

        public PlotViewModel(IDataFeed feed)
        {
            feed.BatchReady += OnBatch;
            feed.Start();

            //_uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / TargetFps) };
            //_uiTimer.Tick += OnUiTick;
            //_uiTimer.Start();
            _uiTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(10),
            DispatcherPriority.Render,
            (_, _) => OnUiTick());
        }

        private void OnBatch(DataBatch batch) => _pending.Enqueue(batch);

        private void OnUiTick()
        {
            if (model.SeriesList.Count != 0)
            {
                model.SeriesList[0].YOffset = 100;
                model.SeriesList[0].YScale = 50;
            }

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

            // append X once (shared timebase)
            Model.TimeSeries.AddRange(b.X);
            // append Ys per series
            for (int s = 0; s < b.Y.Count; s++)
                Model.SeriesList[s].YCoords.AddRange(b.Y[s]);
        }

        private void AutoScrollTail()
        {
            if (Model.TimeSeries.Count == 0) return;
            double xmax = Model.TimeSeries[^1];
            double w = Model.XMax - Model.XMin;
            if (w <= 0) return;
            Model.XMax = xmax;
            Model.XMin = xmax - w;
        }

        //private void ApplyDecimation()
        //{
        //    var (i0, i1) = Model.GetVisibleRangeIndices();
        //    if (i1 <= i0) { foreach (var s in Model.SeriesList) s.DecimatedIndices.Clear(); return; }

        //    int visible = i1 - i0 + 1;
        //    if (visible <= MaxVisiblePtsPerSeries)
        //    {
        //        foreach (var s in Model.SeriesList) s.DecimatedIndices.Clear();
        //        return;
        //    }

        //    foreach (var s in Model.SeriesList)
        //        Decimator.BucketMinMax(Model.TimeSeries, s.YCoords, i0, i1, MaxVisiblePtsPerSeries, s.DecimatedIndices);
        //}

        // IPlotInteractor (from Canvas2D)
        public void OnPanStart(Avalonia.Point a) { }
        public void OnPanDelta(Avalonia.Point d) { Model.Pan(-d.X, -d.Y); RenderVersion++; }
        public void OnPanEnd() { }
    }
}
