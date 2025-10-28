using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using PlotterNew.Models;
using PlotterNew.Services;
using PlotterNew.ViewModels;
using SkiaSharp;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Timers;
using System.Transactions;
using Tmds.DBus.Protocol;
using static System.Net.Mime.MediaTypeNames;

namespace PlotterNew.Controls
{
    public class Canvas : Control
    {
        private MainViewModel? ViewModel => DataContext as MainViewModel;

        SerialTenReader ArduinoPort;
        bool RunWithArduino = true;

        private Point _panOffset = new Point(0, 0);   
        private Point _lastMouse;                     
        private bool _isPanning;
        private bool _isAutoScrolling = true;
        private readonly List<Waveform> _waveforms;
        private readonly List<Services.SineGenerator> _sineGenerators;

        //private readonly ConcurrentQueue<(int idx, List<Point> pts)> _pending = new();

        private readonly ConcurrentQueue<List<Point>> _pending = new();

        private readonly DispatcherTimer _uiTimer;
        private readonly Timer _dataTimer;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private const double _autoScrollMargin = 50;
        private bool _autoScrollEnabled = true;

        private bool _renderQueued;

        private const int _waveformsAmount = 10;

        private int MinVisibleTimeIndex => Math.Max(0, LowerBound(_waveforms[0].nominalPoints, -_panOffset.X) - 1);
        private int MaxVisibleTimeIndex => Math.Min(_waveforms[0].nominalPoints.Count - 1, UpperBound(_waveforms[0].nominalPoints, -_panOffset.X + Bounds.Width) + 1);
        private double MinVisibleTime => _waveforms[0].nominalPoints[MinVisibleTimeIndex].X;
        private double MaxVisibleTime => _waveforms[0].nominalPoints[MaxVisibleTimeIndex].X;

        private double LastTime => _waveforms[0].nominalPoints.Count > 0 ? _waveforms[0].nominalPoints[_waveforms[0].nominalPoints.Count - 1].X : 0.0;
        class TimeRect
        { 
            public double startTime;
            public double endTime;
            public SolidColorBrush fillColor;
            public Pen outlineColor;
        }
        
        private List<TimeRect> _timeRects = new List<TimeRect>();
        private bool _isTimeRectBeingDrawn = false;

        private static double _xScale = 1;
        private const double _minXScale = 1;
        private const double _maxXScale = 50.0;
        private const double _zoomStep = 1.1;
        private const double _wheelPanFactorPx = 20.0;
        private double _wheelPanRemainderPx = 0.0;
        // tune this: world units per wheel "tick"
        private const double _wheelWorldStep = 120.0;

        private bool _isRedRectBeingDrawn = false;
        private  double kRedRectDuration = 5.0 * _xScale * 29; 
        public Canvas()
        {
            if (RunWithArduino)
            {
                ArduinoPort = new SerialTenReader("COM11", 230400);
                ArduinoPort.Start();
            }

            //PointerPressed += OnPointerPressed;
            //PointerReleased += OnPointerReleased;
            //PointerMoved += OnPointerMoved;
            Focusable = true;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            PointerWheelChanged += OnPointerWheelChanged;

            _uiTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(20),
            DispatcherPriority.Render,
            (_, _) => OnUiTick());

            _dataTimer = new Timer(10) { AutoReset = true };
            _dataTimer.Elapsed += (_, __) => OnDataTick();
            
            _waveforms = Waveform.CreateMultiple(_waveformsAmount);
            _sineGenerators = Services.SineGenerator.CreateMultiple(_waveformsAmount);
        }
        private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (Bounds.Width <= 0) return;

            // SHIFT + wheel => horizontal pan in *pixels*
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

            if (shift)
            {
                // prefer horizontal delta on trackpads
                double raw = Math.Abs(e.Delta.X) > 0 ? e.Delta.X : e.Delta.Y;

                // convert a world step into pixels so scroll "speed" is consistent across zooms
                double stepPx = (_wheelWorldStep * _xScale);

                // accumulate fractional pixels to avoid stutter
                double deltaPxAcc = -raw * stepPx + _wheelPanRemainderPx;
                int deltaPxInt = (int)Math.Truncate(deltaPxAcc);            // keep sign
                _wheelPanRemainderPx = deltaPxAcc - deltaPxInt;             // remainder

                if (deltaPxInt != 0)
                {
                    _panOffset = new Point(_panOffset.X + deltaPxInt, _panOffset.Y);
                    ClampPanX();
                    QueueRender();
                }
                e.Handled = true;
                return;
            }

            double mouseX = e.GetPosition(this).X;      // screen/pixel
            double oldScale = _xScale;
            double desired = e.Delta.Y > 0 ? _zoomStep : 1.0 / _zoomStep;
            double newScale = Math.Clamp(oldScale * desired, _minXScale, _maxXScale);
            if (Math.Abs(newScale - oldScale) < 1e-9) return;

            double factor = newScale / oldScale;

            // keep the time under the cursor fixed:
            double newPanX = _panOffset.X + (1 - factor) * (mouseX - _panOffset.X);

            _xScale = newScale;
            _panOffset = new Point(newPanX, _panOffset.Y);

            ClampPanX();
            QueueRender();
        }

        private void OnKeyUp(object? s, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
                _isAutoScrolling = true;
        }

        private void ClampPanX()
        {
            if (_panOffset.X > 0) _panOffset = new Point(0, _panOffset.Y);
            if (_waveforms[0].nominalPoints.Count == 0 || Bounds.Width <= 0) return;

            double lastTime = _waveforms[0].nominalPoints[^1].X;
            double contentWidthPx = lastTime * _xScale;
            double minPanX = Math.Min(0, Bounds.Width - contentWidthPx);
            if (_panOffset.X < minPanX)
                _panOffset = new Point(minPanX, _panOffset.Y);
        }

        public void UpdateWaveformParameters(int idx, double scale, double offset)
        {
            if (idx < 0 || idx >= _waveforms.Count)
                return;
            _waveforms[idx].scale = scale;
            _waveforms[idx].verticalOffset = offset;
            QueueRender();
        }
        public (double, double) GetWaveformParameters(int idx)
        {
            if (idx < 0 || idx >= _waveforms.Count)
                return (1.0, 0.0);
            return (_waveforms[idx].scale, _waveforms[idx].verticalOffset);
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

        /*private void OnDataTick()
        {
            double t = _stopwatch.Elapsed.TotalSeconds;

            if (RunWithArduino)
            {
                var data = ArduinoPort.GetSData();
                if (data == null) return;
                for (int i = 0; i < data.Count && i < _waveforms.Count; i++)
                {
                    var p = new Point(t, data[i]);
                    _pending.Enqueue((i, new List<Point> { p }));
                }
                return;
            }
            else
            {
                for (int i = 0; i < _sineGenerators.Count; i++)
                {
                    if (i == 3)
                    {
                        var randP = _sineGenerators[i].GetRandomAmplitudePoint(t);
                        _pending.Enqueue((i, new List<Point> { randP }));
                        continue;
                    }

                    var p = _sineGenerators[i].GetPoint(t);
                    _pending.Enqueue((i, new List<Point> { p }));
                }
            }
        }*/
        //private void OnDataTick()
        //{
        //    double t = _stopwatch.Elapsed.TotalSeconds;
        //    var series = new Point?[_waveforms.Count];   // one slot per plot

        //    if (RunWithArduino)
        //    {
        //        var data = ArduinoPort.GetSData();
        //        if (data == null) return;

        //        int n = Math.Min(data.Count, _waveforms.Count);
        //        for (int i = 0; i < n; i++)
        //            series[i] = new Point(t, data[i]);
        //    }
        //    else
        //    {
        //        for (int i = 0; i < _sineGenerators.Count && i < _waveforms.Count; i++)
        //        {
        //            if (i == 3)
        //                series[i] = _sineGenerators[i].GetRandomAmplitudePoint(t);
        //            else
        //                series[i] = _sineGenerators[i].GetPoint(t);
        //        }
        //    }

        //    _pending.Enqueue(new Batch(t, series));
        //}

        //public static readonly StyledProperty<string> LogTextProperty =
        //AvaloniaProperty.Register<Controls.Canvas, string>(nameof(LogText), defaultValue: string.Empty);

        //public string LogText
        //{
        //    get => GetValue(LogTextProperty);
        //    set => SetValue(LogTextProperty, value);
        //}

        private void OnDataTick()
        {
            if (RunWithArduino)
            {
                if (ArduinoPort.Queue.TryDequeue(out var ten))
                {
                    double t = _stopwatch.Elapsed.TotalSeconds;
                    List<Point> chunk = new List<Point>(_waveformsAmount);
                    for (int i = 0; i < _waveformsAmount; i++)
                    {
                        chunk.Add(new Point(t, ten.Values[i]));
                    }
                    _pending.Enqueue(chunk);
                }
            }
            else
            {
                double t = _stopwatch.Elapsed.TotalSeconds;
                List<Point> chunk = new List<Point>(_waveformsAmount);
               
                for (int i = 0; i < _waveformsAmount; i++)
                {
                    var p = (i == 3)
                        ? _sineGenerators[i].GetRandomAmplitudePoint(t)
                        : _sineGenerators[i].GetPoint(t);
                    chunk.Add(p);
                }
                _pending.Enqueue(chunk);
            }

            Dispatcher.UIThread.Post(() =>
            {
                QueueRender();
            }, DispatcherPriority.Render);
        }

        /*private void OnUiTick()
        {
            while (_pending.TryDequeue(out var item))
            {
                _waveforms[item.idx].nominalPoints.AddRange(item.pts);
            }

            if (_isAutoScrolling)
            {
                TryAutoScroll();
            }

            UpdateViewModel();

            InvalidateVisual();
        }*/
        //private void OnUiTick()
        //{
        //    // Collect all dequeued batches into per-series buckets
        //    var buckets = new List<Point>[_waveforms.Count];

        //    while (_pending.TryDequeue(out var batch))
        //    {
        //        var series = batch.Series;
        //        int n = Math.Min(series.Length, _waveforms.Count);

        //        for (int i = 0; i < n; i++)
        //        {
        //            if (series[i] is Point p)
        //            {
        //                (buckets[i] ??= new List<Point>(16)).Add(p);
        //            }
        //        }
        //    }

        //    // Append once per series to reduce churn
        //    for (int i = 0; i < buckets.Length; i++)
        //    {
        //        if (buckets[i] is { Count: > 0 } list)
        //            _waveforms[i].nominalPoints.AddRange(list);
        //    }

        //    if (_isAutoScrolling)
        //        TryAutoScroll();

        //    UpdateViewModel();
        //    InvalidateVisual();
        //}
        private void OnUiTick()
        {
            // Drain all available full chunks
            while (_pending.TryDequeue(out var chunk))
            {
                for (int i = 0; i < _waveformsAmount; i++)
                {
                    _waveforms[i].nominalPoints.Add(chunk[i] * 10);
                }
            }

            if (_isAutoScrolling)
                TryAutoScroll();

            UpdateViewModel();
            InvalidateVisual();
        }
        private void TryAutoScroll()
        {
            if (!_autoScrollEnabled || _isPanning) return;
            if (Bounds.Width <= 0) return;
            if (_waveforms[0].nominalPoints.Count == 0) return;

            double minWorldX = (-_panOffset.X) / _xScale;
            double viewWidthWorld = Bounds.Width / _xScale;
            double rightWorld = minWorldX + viewWidthWorld;

            double lastTime = _waveforms[0].nominalPoints[^1].X; // max world X
            double marginWorld = _autoScrollMargin / _xScale;

            if (lastTime > rightWorld - marginWorld)
            {
                double newLeftWorld = lastTime - viewWidthWorld + marginWorld;
                double newPanX = -newLeftWorld * _xScale;
                newPanX = Math.Min(0, newPanX);

                if (Math.Abs(newPanX - _panOffset.X) > 0.1)
                    _panOffset = new Point(newPanX, _panOffset.Y);
            }
        }

        public void UpdateViewModel()
        {
            App.MainVM.CurrPanX = _panOffset.X;
            App.MainVM.CurrPanY = _panOffset.Y;
            App.MainVM.ElapsedTime = _stopwatch.Elapsed.TotalSeconds;
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
            if (e.Key == Key.F)
            {
                if (!_isTimeRectBeingDrawn)
                {
                    if (_waveforms.Count == 0 || _waveforms[0].nominalPoints.Count == 0)
                        return;

                    _isTimeRectBeingDrawn = true;
                    _timeRects.Add(new TimeRect
                    {
                        startTime = LastTime,
                        endTime = LastTime,
                        fillColor = new SolidColorBrush(Color.FromArgb(64, 255, 255, 0)),
                        outlineColor = new Pen(Brushes.Yellow, 2)
                    });
                }
                else
                {
                    _isTimeRectBeingDrawn = false;
                    _isRedRectBeingDrawn = true;
                    _timeRects.Add(new TimeRect
                    {
                        startTime = LastTime,
                        endTime = LastTime,
                        fillColor = new SolidColorBrush(Color.FromArgb(64, 255, 0, 0)),
                        outlineColor = new Pen(Brushes.DarkRed, 2)
                    });
                }
            }
            else if (e.Key == Key.Space)
            {
                ToggleTimers();
            }
            else if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
                _isAutoScrolling = false;

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

        static List<int> FindDivisibleIntegers(double minVal, double maxVal, int divisor)
        {
            var result = new List<int>();

            int start = (int)Math.Ceiling(minVal);
            int end = (int)Math.Floor(maxVal);

            // First number divisible by divisor and >= start
            int firstDivisible = ((start + divisor - 1) / divisor) * divisor;

            for (int val = firstDivisible; val <= end; val += divisor)
                result.Add(val);

            return result;
        }

        public static string ToMinutesSeconds(int totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0; // optional guard

            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;

            return $"{minutes:D2}:{seconds:D2}";
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

            double minWorldX = (-_panOffset.X) / _xScale;
            double maxWorldX = (-_panOffset.X + Bounds.Width) / _xScale;

            int start = LowerBound(_waveforms[0].nominalPoints, minWorldX);
            int end = UpperBound(_waveforms[0].nominalPoints, maxWorldX);

            if (start < end && start <= _waveforms[0].nominalPoints.Count)
            {
                start = Math.Max(0, start);
                end = Math.Min(_waveforms[0].nominalPoints.Count - 1, end - 1);

                for(int i = 0; i < _waveforms.Count; ++i)
                {
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(CalcTransformedPoint(i, start), false);
                        for (int j = start + 1; j <= end; j++)
                        {
                            g.LineTo(CalcTransformedPoint(i, j));
                        }
                        g.EndFigure(false);
                    }

                    Pen penForThisWaveform = PredefinedPens.Get(i);
                    context.DrawGeometry(null, penForThisWaveform, geo);

                    var scb = (ISolidColorBrush)penForThisWaveform.Brush;
                    var pen = new Pen(new SolidColorBrush(scb.Color, 0.3), 2);

                    context.DrawLine(pen, 
                        new Point(_waveforms[0].nominalPoints[start].X * _xScale, ViewModel!.SliderCentersY[i]), 
                        new Point(_waveforms[0].nominalPoints[end].X * _xScale, ViewModel!.SliderCentersY[i]));
                }

                int timeInterval = 1;

                List<int> timeLabelValues = FindDivisibleIntegers(_waveforms[0].nominalPoints[start].X, _waveforms[0].nominalPoints[end].X, timeInterval * 30);

                foreach (int val in timeLabelValues)
                {
                    int valToPring = val / 30;
                    var pt = new Point(val * _xScale, Bounds.Height - 50 - _panOffset.Y);
                    Avalonia.Media.FormattedText timeTextBuffer = new FormattedText(
                                    ToMinutesSeconds(valToPring),
                                    CultureInfo.InvariantCulture,
                                    FlowDirection.LeftToRight,
                                    new Typeface("Segoe UI"),
                                    24,
                                    Brushes.White);

                    context.DrawText(timeTextBuffer, pt);

                    var dashedPen = new Pen(
                        new SolidColorBrush(Colors.White, 0.5),
                        2,
                        new DashStyle(new double[] { 6, 4 }, 0)
                    );
                    context.DrawLine(dashedPen, new Point(val * _xScale + 29, -_panOffset.Y), new Point(val * _xScale + 29, Bounds.Height - _panOffset.Y));
                }

                if (_isTimeRectBeingDrawn && _timeRects.Count > 0)
                {
                    _timeRects[_timeRects.Count - 1].endTime = _waveforms[0].nominalPoints[_waveforms[0].nominalPoints.Count - 1].X;
                }

                if (_isRedRectBeingDrawn && _timeRects.Count > 0)
                {
                    _timeRects[_timeRects.Count - 1].endTime = _waveforms[0].nominalPoints[_waveforms[0].nominalPoints.Count - 1].X;

                    if (_timeRects[_timeRects.Count - 1].endTime - _timeRects[_timeRects.Count - 1].startTime >= kRedRectDuration)
                    {
                        _isRedRectBeingDrawn = false;
                    }
                }
                foreach (var timeRect in _timeRects)
                {
                    if (_waveforms[0].nominalPoints[start].X < timeRect.endTime && _waveforms[0].nominalPoints[end].X > timeRect.startTime)
                    {
                        var rect = new Rect(
                            timeRect.startTime * _xScale,
                            -_panOffset.Y,
                            (timeRect.endTime - timeRect.startTime) * _xScale,
                            Bounds.Height);

                        context.DrawRectangle(timeRect.fillColor, timeRect.outlineColor, rect);
                    }
                }
            }
        }


        private Point CalcTransformedPoint(int waveformIdx, int pointIdx)
        {
            var wf = _waveforms[waveformIdx];
            var pt = wf.nominalPoints[pointIdx];
            double x = pt.X * _xScale; 
            double y = pt.Y * ViewModel.SliderValues[waveformIdx] + ViewModel.SliderCentersY[waveformIdx];
            return new Point(x, y);
        }

        private static int LowerBound(List<Avalonia.Point> pts, double x)
        {
            int lo = 0, hi = pts.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (pts[mid].X < x) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private static int UpperBound(List<Avalonia.Point> pts, double x)
        {
            int lo = 0, hi = pts.Count; // [lo, hi)
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (pts[mid].X <= x) lo = mid + 1;
                else hi = mid;
            }
            return lo - 1;
        }
    }
}
