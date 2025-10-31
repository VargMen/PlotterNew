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
        bool RunWithArduino = false;
        private MainViewModel? ViewModel => DataContext as MainViewModel;

        private const int _waveformsAmount = 10;
        private List<Waveform> _waveforms;

        class TimeMarker
        {
            public double time = 0.0;
            public Pen pen;
            public SolidColorBrush flagColor;
            public Pen flagOutlineColor;
        }

        class TimeRect
        {
            public double startTime = 0.0;
            public double endTime = 0.0;
            public SolidColorBrush fillColor;
            public Pen outlineColor;
            public List<TimeMarker> markers;
        }

        private List<TimeRect> _timeRects = new List<TimeRect>();
        private bool _isTimeRectBeingDrawn = false;

        private List<Services.SineGenerator> _sineGenerators;

        private SerialReader32 _serialReader;
        private ConcurrentQueue<List<Point>> _pending = new();

        private Timer _dataTimer;
        private Stopwatch _stopwatch = new Stopwatch();

        private DispatcherTimer _uiTimer;
        private bool _renderQueued = false;

        private bool _isAutoScrolling = true;
        private Point _panOffset = new Point(0, 0);
        private Point _lastMouse = new Point(0, 0);
        private bool _isPanning = false;

        private static double _xScale = 1;
        private const double _minXScale = 1;
        private const double _maxXScale = 300.0;
        private const double _zoomStep = 1.1;
        private double _wheelPanRemainderPx = 0.0;
        private const double _wheelWorldStep = 10.0;

        // Time rectangle drawing state
        private bool _isRedRectBeingDrawn = false;
        private double kRedRectDuration = 8.0 * _xScale;

        private double _autoTargetPanX = 0.0;       // updated by TryAutoScroll
        private double _autoPanVelX = 0.0;          // velocity for the smooth damp
        private double _lastAnimSec = 0.0;

        //private int MinVisibleTimeIndex => Math.Max(0, LowerBound(_waveforms[0].nominalPoints, -_panOffset.X) - 1);
        //private int MaxVisibleTimeIndex => Math.Min(_waveforms[0].nominalPoints.Count - 1, UpperBound(_waveforms[0].nominalPoints, -_panOffset.X + Bounds.Width) + 1);
        //private double MinVisibleTime => _waveforms[0].nominalPoints[MinVisibleTimeIndex].X;
        //private double MaxVisibleTime => _waveforms[0].nominalPoints[MaxVisibleTimeIndex].X;

        private double LastTime => _waveforms[0].nominalPoints.Count > 0 ? _waveforms[0].nominalPoints[_waveforms[0].nominalPoints.Count - 1].X : 0.0;

        public Canvas()
        {
            if (RunWithArduino)
            {
                _serialReader = new SerialReader32("COM11", 230400);
                _serialReader.Start();
            }

            _waveforms = Waveform.CreateMultiple(_waveformsAmount);
            _sineGenerators = Services.SineGenerator.CreateMultiple(_waveformsAmount);

            Focusable = true;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            PointerWheelChanged += OnPointerWheelChanged;

            _uiTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(17),
            DispatcherPriority.Render,
            (_, _) => OnUiTick());

            _dataTimer = new Timer(55) { AutoReset = true };
            _dataTimer.Elapsed += (_, __) => OnDataTick();
        }
        private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (Bounds.Width <= 0) return;

            if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
            {
                double raw = e.Delta.Y;

                // convert a world step into pixels so scroll "speed" is consistent across zooms
                double stepPx = (_wheelWorldStep + _xScale / 10);

                // accumulate fractional pixels to avoid stutter
                double deltaPxAcc = -raw * stepPx + _wheelPanRemainderPx;
                int deltaPxInt = (int)Math.Truncate(deltaPxAcc);            // keep sign
                _wheelPanRemainderPx = deltaPxAcc - deltaPxInt;             // remainder

                if (deltaPxInt != 0)
                {
                    _panOffset = new Point(_panOffset.X + deltaPxInt, _panOffset.Y);
                }
            }
            else
            {
                double mouseX = e.GetPosition(this).X;      // screen/pixel
                double oldScale = _xScale;
                double desired = e.Delta.Y > 0 ? _zoomStep : 1.0 / _zoomStep;
                double newScale = Math.Clamp(oldScale * desired, _minXScale, _maxXScale);
                if (Math.Abs(newScale - oldScale) < 1.0e-5) return;

                double factor = newScale / oldScale;

                // keep the time under the cursor fixed:
                double newPanX = _panOffset.X + (1 - factor) * (mouseX - _panOffset.X);

                _xScale = newScale;
                _panOffset = new Point(newPanX, _panOffset.Y);
            }

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

            double lastTime = LastTime;
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

        private void OnDataTick()
        {
            if (RunWithArduino)
            {
                if (_serialReader.Queue.TryDequeue(out var pkt))
                {
                    List<Point> chunk = new List<Point>(_waveformsAmount);
                    for (int i = 0; i < _waveformsAmount; i++)
                    {
                        chunk.Add(new Point(pkt.TimeSec, pkt.Values[i]));
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
        }
        private void OnUiTick()
        {
            if (_pending.TryDequeue(out var chunk))
            {
                for (int i = 0; i < _waveformsAmount; i++)
                {
                    _waveforms[i].nominalPoints.Add(chunk[i]);
                }
            }

            if (_isAutoScrolling && !_isPanning)
                TryAutoScroll();

            UpdateViewModel();
            InvalidateVisual();
        }
        /*private void TryAutoScroll()
        {
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
        }*/
        private const double _marginStartPx = 80;   // start auto-scroll when newest point gets this close
        private const double _marginStopPx = 120;  // use a bit bigger margin to avoid chatter (hysteresis)
        private const double _smoothTimeSec = 0.10; // ~100 ms response (tweak)
        private const double _maxSpeedPxSec = 5000; // clamp excessive speeds (tweak)

        private void TryAutoScroll()
        {
            if (Bounds.Width <= 0) return;
            if (_waveforms.Count == 0 || _waveforms[0].nominalPoints.Count == 0) return;

            double minWorldX = (-_panOffset.X) / _xScale;
            double viewWidthWorld = Bounds.Width / _xScale;
            double rightWorld = minWorldX + viewWidthWorld;

            double lastTime = _waveforms[0].nominalPoints[^1].X; // most recent world X

            double marginStartWorld = _marginStartPx / _xScale;
            double marginStopWorld = _marginStopPx / _xScale;

            if (lastTime > rightWorld - marginStopWorld)
            {
                // Keep newest point at ~_marginStopPx from the right edge
                double newLeftWorld = lastTime - viewWidthWorld + marginStopWorld;
                double targetPanX = -newLeftWorld * _xScale;

                // Don’t allow panning into positive (empty space on the left)
                _autoTargetPanX = Math.Min(0.0, targetPanX);
            }
            AnimatePan();
        }

        private static double SmoothDamp(double current, double target, ref double currentVelocity,
                                 double smoothTime, double maxSpeed, double deltaTime)
        {
            smoothTime = Math.Max(0.0001, smoothTime);
            double omega = 2.0 / smoothTime;
            double x = omega * deltaTime;
            double exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);

            double change = current - target;
            double maxChange = maxSpeed * smoothTime;
            change = Math.Clamp(change, -maxChange, maxChange);

            double temp = (currentVelocity + omega * change) * deltaTime;
            currentVelocity = (currentVelocity - omega * temp) * exp;

            double output = target + (change + temp) * exp;

            // Prevent overshoot
            if ((target - current > 0.0) == (output > target))
            {
                output = target;
                currentVelocity = 0.0;
            }
            return output;
        }

        private void AnimatePan()
        {
            double now = _stopwatch.Elapsed.TotalSeconds;
            double dt = Math.Max(0.0, now - _lastAnimSec);
            _lastAnimSec = now;

            if (_isAutoScrolling)
            {
                double newX = SmoothDamp(_panOffset.X, _autoTargetPanX, ref _autoPanVelX,
                                         _smoothTimeSec, _maxSpeedPxSec, dt);

                if (Math.Abs(newX - _panOffset.X) > 0.01) // tiny deadzone
                {
                    _panOffset = new Point(newX, _panOffset.Y);
                    // You likely already call QueueRender() elsewhere; keep it cheap here.
                }
            }
            else
            {
                // if user is panning manually, decay velocity so it doesn’t “snap back” later
                _autoPanVelX *= Math.Exp(-6.0 * dt);
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
                        outlineColor = new Pen(Brushes.Yellow, 2),
                        markers = new List<TimeMarker>()
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
                        outlineColor = new Pen(Brushes.DarkRed, 2),
                        markers = new List<TimeMarker>()
                    });
                }
            }
            else if (e.Key == Key.Space)
            {
                ToggleTimers();
            }
            else if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                _isAutoScrolling = false;
            }
            else if (e.Key == Key.OemPlus)
            {
                if (_isRedRectBeingDrawn)
                {
                    _timeRects[_timeRects.Count - 1].markers.Add(new TimeMarker 
                    { 
                        time = LastTime, 
                        pen = new Pen(Brushes.White), 
                        flagColor = new SolidColorBrush(Color.FromArgb(180, 0, 255, 0)),
                        flagOutlineColor = new Pen(Brushes.Green) 
                    });
                }
            }
            else if (e.Key == Key.OemMinus)
            {
                if (_isRedRectBeingDrawn)
                {
                    _timeRects[_timeRects.Count - 1].markers.Add(new TimeMarker
                    {
                        time = LastTime,
                        pen = new Pen(Brushes.White),
                        flagColor = new SolidColorBrush(Color.FromArgb(180, 255, 0, 0)),
                        flagOutlineColor = new Pen(Brushes.Red)
                    });
                }
            }
            //else if (e.Key == Key.Q)
            //{
            //    for (var value in App.MainVM.SliderValues)
            //    {
            //        value += 2;
            //    }
            //}

                QueueRender();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _isPanning = true;
            _isAutoScrolling = false;
            _lastMouse = e.GetPosition(this);
            e.Pointer.Capture(this);
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _isPanning = false;
            _isAutoScrolling = true;
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

        void DrawVerticalLine(DrawingContext ctx, double xCoord, Pen pen)
        {
            ctx.DrawLine(pen, new Point(xCoord, -_panOffset.Y), new Point(xCoord, Bounds.Height - _panOffset.Y));
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

                        if (_waveforms[i].nominalPoints.Last().X < _panOffset.X + Bounds.Width * _xScale)
                        {
                            g.LineTo(new Point(_waveforms[i].nominalPoints.Last().X * _xScale, App.MainVM.SliderCentersY[i]));
                            //g.LineTo(new Point(_waveforms[i].nominalPoints.Last().X + 10, CalcTransformedPoint(i, 0).Y));
                            g.LineTo(new Point(_panOffset.X + Bounds.Width * _xScale, App.MainVM.SliderCentersY[i]));
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

                int timeInterval = 5;

                List<int> timeLabelValues = FindDivisibleIntegers(_waveforms[0].nominalPoints[start].X, _waveforms[0].nominalPoints[end].X, timeInterval);

                foreach (int val in timeLabelValues)
                {
                    int valToPring = val;
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

                    DrawVerticalLine(context, val * _xScale + 29, dashedPen);
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

                        foreach (var timeMarker in timeRect.markers)
                        {
                            DrawVerticalLine(context, timeMarker.time * _xScale, timeMarker.pen);

                            var flag = new Rect(
                            timeMarker.time * _xScale,
                            -_panOffset.Y + 40,
                            50,
                            35);

                            context.DrawRectangle(timeMarker.flagColor, timeMarker.flagOutlineColor, flag);
                        }
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
