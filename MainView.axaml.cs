using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace PlotterNew
{
    public partial class MainView : Window
    {
        // Layout / slider geometry
        private const int SliderCount = 10;
        private const double SliderWidth = 45;
        private const double SliderHeight = 25;
        private const double SidePadding = 30;

        // Data: center Y for each slider (in Canvas coordinates)
        private readonly double[] _centersY = new double[SliderCount];

        // Dragging state
        private int _dragIndex = -1;
        private double _dragOffsetY; // pointerY - currentTop


        public MainView()
        {
            InitializeComponent();

            // Build sliders once the canvas is realized
            SliderCanvas.AttachedToVisualTree += (_, __) => BuildSliders();
            SliderCanvas.SizeChanged += (_, __) => ClampAllToCanvas();
            //CentersList.ItemsSource = _centersView;
            // Show centers on the right column
            UpdateCentersView();
        }

        private void BuildSliders()
        {
            SliderCanvas.Children.Clear();

            double usableWidth = Math.Max(0, SliderCanvas.Bounds.Width - 2 * SidePadding);
            double spacing = usableWidth / SliderCount;

            for (int i = 0; i < SliderCount; i++)
            {
                var rect = new Border
                {
                    Width = SliderWidth,
                    Height = SliderHeight,
                    Background = Brushes.SlateGray,
                    CornerRadius = new CornerRadius(4),
                    Tag = i,
                    Cursor = new Cursor(StandardCursorType.SizeNorthSouth)
                };

                // Horizontal position (fixed)
                double x = SidePadding + i * spacing + (spacing - SliderWidth) / 2.0;
                Canvas.SetLeft(rect, x);

                // Initial vertical position: centered
                double top = (SliderCanvas.Bounds.Height - SliderHeight) / 2.0;
                Canvas.SetTop(rect, top);
                _centersY[i] = top + SliderHeight / 2.0;

                // Pointer events
                rect.PointerPressed += Slider_PointerPressed;
                rect.PointerMoved += Slider_PointerMoved;
                rect.PointerReleased += Slider_PointerReleased;
                rect.PointerCaptureLost += Slider_PointerCaptureLost;

                SliderCanvas.Children.Add(rect);
            }

            UpdateCentersView();
        }

        private void Slider_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border rect) return;
            if (!e.GetCurrentPoint(rect).Properties.IsLeftButtonPressed) return;

            _dragIndex = (int)rect.Tag!;
            rect.Focus(); // optional
            e.Pointer.Capture(rect);

            var pOnCanvas = e.GetPosition(SliderCanvas);
            double currentTop = Canvas.GetTop(rect);
            _dragOffsetY = pOnCanvas.Y - currentTop;

            e.Handled = true;
        }

        private void Slider_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_dragIndex < 0) return;
            if (sender is not Border rect) return;

            var pOnCanvas = e.GetPosition(SliderCanvas);

            double newTop = pOnCanvas.Y - _dragOffsetY;
            newTop = Clamp(newTop, 0, Math.Max(0, SliderCanvas.Bounds.Height - SliderHeight));

            Canvas.SetTop(rect, newTop);
            _centersY[_dragIndex] = newTop + SliderHeight / 2.0;

            UpdateCentersView();
            e.Handled = true;
        }

        private void Slider_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is Border rect)
                e.Pointer.Capture(null);
            _dragIndex = -1;
            e.Handled = true;
        }

        private void Slider_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            _dragIndex = -1;
        }

        private void ClampAllToCanvas()
        {
            // Ensure sliders remain inside new height after resize
            foreach (var child in SliderCanvas.Children)
            {
                if (child is not Border rect) continue;
                double top = Canvas.GetTop(rect);
                double clamped = Clamp(top, 0, Math.Max(0, SliderCanvas.Bounds.Height - SliderHeight));
                if (Math.Abs(clamped - top) > double.Epsilon)
                    Canvas.SetTop(rect, clamped);

                int idx = (int)rect.Tag!;
                _centersY[idx] = clamped + SliderHeight / 2.0;
            }
            UpdateCentersView();
        }

        private static double Clamp(double v, double lo, double hi) =>
            v < lo ? lo : (v > hi ? hi : v);

        // Expose centers if you want to read them elsewhere
        public IReadOnlyList<double> CentersY => _centersY;

        // Simple UI on the right to show values
        private void UpdateCentersView()
        {
            App.MainVM.SliderCentersY = new ObservableCollection<double>(_centersY);
        }


    }
}