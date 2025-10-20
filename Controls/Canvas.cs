using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using PlotterNew.Models;
using PlotterNew.Services;
using PlotterNew.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;

namespace PlotterNew.Controls
{
    public class Canvas : Control
    {
        private Point _panOffset = new Point(0, 0);   
        private Point _lastMouse;                     
        private bool _isPanning;

        private readonly List<Waveform> _waveforms;
        private readonly List<Services.SineGenerator> _sineGenerators;

        private readonly ConcurrentQueue<(int idx, List<Point> pts)> _pending = new();

        private const double _amplitudeStep = 0.1;
        private const double _yPositionStep = 10;

        private readonly DispatcherTimer _uiTimer;
        private readonly Timer _dataTimer;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private const double _autoScrollMargin = 50;
        private bool _autoScrollEnabled = true;

        private bool _renderQueued;

        public Canvas()
        {
            PointerPressed += OnPointerPressed;
            PointerReleased += OnPointerReleased;
            PointerMoved += OnPointerMoved;
            Focusable = true;
            KeyDown += OnKeyDown;

            _uiTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(10),
            DispatcherPriority.Render,
            (_, _) => OnUiTick());


            _dataTimer = new Timer(10) { AutoReset = true };
            _dataTimer.Elapsed += (_, __) => OnDataTick();

            const int _waveformsAmount = 3;
            _waveforms = Waveform.CreateMultiple(_waveformsAmount);
            _sineGenerators = Services.SineGenerator.CreateMultiple(_waveformsAmount);
        }

        private void QueueRender()
        {
            if (_renderQueued) return;
            _renderQueued = true;

            Dispatcher.UIThread.Post(() =>
            {
                _renderQueued = false;
                InvalidateVisual();
            }, DispatcherPriority.Render);
        }

        private void OnDataTick()
        {
            double t = _stopwatch.Elapsed.TotalSeconds;

            for (int i = 0; i < _sineGenerators.Count; i++)
            {
                var p = _sineGenerators[i].GetPoint(t);
                _pending.Enqueue((i, new List<Point> { p }));
            }
        }

        private void OnUiTick()
        {
            while (_pending.TryDequeue(out var item))
            {
                _waveforms[item.idx].nominalPoints.AddRange(item.pts);
            }

            TryAutoScroll();

            UpdateViewModel((MainViewModel)DataContext);

            InvalidateVisual();
        }

        private void TryAutoScroll()
        {
            if (!_autoScrollEnabled || _isPanning)
                return;

            double viewWidth = Bounds.Width;
            if (!(viewWidth > 0))
                return;

            double leftEdge = -_panOffset.X;
            double rightEdge = leftEdge + viewWidth;

            if (_waveforms[0].nominalPoints.Count == 0)
                return;

            double latestX = _waveforms[0].nominalPoints.Last().X;

            if (latestX > rightEdge - _autoScrollMargin)
            {
                double newLeft = latestX - viewWidth + _autoScrollMargin;
                double newPanX = -newLeft;

                newPanX = Math.Min(0, newPanX);

                if (Math.Abs(newPanX - _panOffset.X) > 0.01)
                {
                    _panOffset = new Point(newPanX, _panOffset.Y);
                }
            }
        }

        public void UpdateViewModel(MainViewModel vm)
        {
            vm.CurrPanX = _panOffset.X;
            vm.CurrPanY = _panOffset.Y;
            vm.ElapsedTime = _stopwatch.Elapsed.TotalSeconds;
        }
        public void Dispose()
        {
            _dataTimer.Dispose();
            _uiTimer.Stop();
        }

        public void ToggleTimers()
        {
             if (_dataTimer.Enabled)
            {
                _dataTimer.Stop();
                _uiTimer.Stop();
                _stopwatch.Stop();
            }
            else
            {
                _dataTimer.Start();
                _uiTimer.Start();
                _stopwatch.Start();
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.W:
                    _waveforms[0].scale += _amplitudeStep;
                    break;
                case Key.S:
                    _waveforms[0].scale -= _amplitudeStep;
                    break;

                case Key.E:
                    _waveforms[0].verticalOffset += _yPositionStep;
                    break;
                case Key.D:
                    _waveforms[0].verticalOffset -= _yPositionStep;
                    break;

                case Key.T:
                    _waveforms[1].scale += _amplitudeStep;
                    break;
                case Key.G:
                    _waveforms[1].scale -= _amplitudeStep;
                    break;

                case Key.Y:
                    _waveforms[1].verticalOffset += _yPositionStep;
                    break;
                case Key.H:
                    _waveforms[1].verticalOffset -= _yPositionStep;
                    break;

                case Key.Space:
                    ToggleTimers();
                    break;
            }

            QueueRender();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _isPanning = true;
            _autoScrollEnabled = false;
            _lastMouse = e.GetPosition(this);
            e.Pointer.Capture(this);
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _isPanning = false;
            _autoScrollEnabled = true;
            e.Pointer.Capture(null);
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isPanning) return;

            var pos = e.GetPosition(this);
            var dx = pos.X - _lastMouse.X;
            var dy = pos.Y - _lastMouse.Y;

            double currPanX = _panOffset.X + dx;
            double currPanY = _panOffset.Y + dy;

            if (currPanX > 0)
                currPanX = 0;

            _panOffset = new Point(currPanX, currPanY);
            _lastMouse = pos;

            QueueRender();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            context.FillRectangle(Brushes.Black, Bounds);

            context.PushTransform(Matrix.CreateTranslation(_panOffset));

            RenderWaveforms(context);
        }
        private void RenderWaveforms(DrawingContext context)
        {
            if (_waveforms[0].nominalPoints.Count == 0)
                return;

            double minViewX = -_panOffset.X;
            double maxViewX = -_panOffset.X + Bounds.Width;

            int start = FindClosestValueIndex(_waveforms[0].nominalPoints, minViewX);
            int end = FindClosestValueIndex(_waveforms[0].nominalPoints, maxViewX);

            if (start < end && start <= _waveforms[0].nominalPoints.Count)
            {
                start = Math.Max(0, start);
                end = Math.Min(_waveforms[0].nominalPoints.Count - 1, end + 1);

                for(int i = 0; i < _waveforms.Count; ++i)
                {
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(_waveforms[i].GetTransformedPoint(start), false);
                        for (int j = start + 1; j <= end; j++)
                        {
                            g.LineTo(_waveforms[i].GetTransformedPoint(j));
                        }
                        g.EndFigure(false);
                    }

                    Pen penForThisWaveform = PredefinedPens.Get(i);
                    context.DrawGeometry(null, penForThisWaveform, geo);
                }
            }
        }
        private void RenderAxes(DrawingContext context)
        {
            var penY = new Pen(Brushes.Green, 4);
            context.DrawLine(penY, new Point(0, 0), new Point(0, 500));

            var penX = new Pen(Brushes.Red, 4);
            context.DrawLine(penX, new Point(0, 0), new Point(500, 0));
        }
        private static int FindClosestValueIndex(List<Avalonia.Point> pts, double x)
        {
            int lo = 0, hi = pts.Count; // [lo, hi)
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (pts[mid].X <= x)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }
            return lo - 1;
        }
    }
}
