using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PlotterNew.Services;
using PlotterNew.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace PlotterNew
{
    public partial class MainView : Window
    {
        private const int SliderCount = 10;
        private const double SliderWidth = 45;
        private const double SliderHeight = 25;
        private const double SidePadding = 30;
        private const double DiamondSize = 16;
        private const double ValueSensitivity = 0.05; // px -> value

        // Drag state for rectangles (move slider)
        private int _dragIndex = -1;
        private double _dragOffsetY;

        // Drag state for diamonds (update value; slider doesn't move)
        private int _valueDragIndex = -1;
        private double _valuePressY;
        private double _valueBaseAtPress;

        private readonly List<Border> _rects = new();
        private readonly List<Border> _diamonds = new();

        private MainViewModel VM => (MainViewModel)DataContext!;

        public MainView()
        {
            InitializeComponent();

            DataContext = new MainViewModel();

            //Services.ArduinoData data = new ArduinoData("COM11", 9600);
            //double[] values = data.GetSData();

            SliderCanvas.AttachedToVisualTree += (_, __) =>
            {
                EnsureCollections();
                BuildSliders();
            };

            SliderCanvas.SizeChanged += (_, __) => RepositionAll();
        }

        private void EnsureCollections()
        {
            while (VM.SliderCentersY.Count < SliderCount) VM.SliderCentersY.Add(0);
            while (VM.SliderValues.Count < SliderCount) VM.SliderValues.Add(0);
        }

        private void BuildSliders()
        {
            SliderCanvas.Children.Clear();
            _rects.Clear();
            _diamonds.Clear();

            double usableWidth = Math.Max(0, SliderCanvas.Bounds.Width - 2 * SidePadding);
            double spacing = usableWidth / SliderCount;

            for (int i = 0; i < SliderCount; i++)
            {
                // === Rectangle (vertical slider) ===
                var rect = new Border
                {
                    Width = SliderWidth,
                    Height = SliderHeight,
                    Background = Services.PredefinedPens.Brushes[i],
                    CornerRadius = new CornerRadius(4),
                    Tag = i,
                    Cursor = new Cursor(StandardCursorType.SizeNorthSouth)
                };

                double x = SidePadding + i * spacing + (spacing - SliderWidth) / 2.0;
                Canvas.SetLeft(rect, x);

                double top = VM.SliderCentersY[i];
                Canvas.SetTop(rect, top);

                //VM.SliderCentersY[i] = top + SliderHeight / 2.0;

                rect.PointerPressed += Slider_PointerPressed;
                rect.PointerMoved += Slider_PointerMoved;
                rect.PointerReleased += Slider_PointerReleased;

                SliderCanvas.Children.Add(rect);
                _rects.Add(rect);

                // === Diamond (rhombus handle) ===
                var diamond = new Border
                {
                    Width = DiamondSize,
                    Height = DiamondSize,
                    Background = Brushes.Orange,
                    Tag = i,
                    RenderTransform = new RotateTransform(45),
                    RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    Cursor = new Cursor(StandardCursorType.SizeNorthSouth)
                };

                CenterDiamond(i, rect, diamond);

                diamond.PointerPressed += Diamond_PointerPressed;
                diamond.PointerMoved += Diamond_PointerMoved;
                diamond.PointerReleased += Diamond_PointerReleased;

                SliderCanvas.Children.Add(diamond);
                _diamonds.Add(diamond);
            }
        }

        private void CenterDiamond(int i, Border rect, Border diamond)
        {
            double x = Canvas.GetLeft(rect);
            double centerY = VM.SliderCentersY[i];

            Canvas.SetLeft(diamond, x + (SliderWidth - DiamondSize) / 2.0);
            Canvas.SetTop(diamond, centerY - DiamondSize / 2.0);
        }

        private void RepositionAll()
        {
            for (int i = 0; i < _rects.Count; i++)
            {
                var rect = _rects[i];
                double top = Canvas.GetTop(rect);
                double clamped = Clamp(top, 0, Math.Max(0, SliderCanvas.Bounds.Height - SliderHeight));
                if (Math.Abs(clamped - top) > double.Epsilon)
                    Canvas.SetTop(rect, clamped);

                VM.SliderCentersY[i] = Canvas.GetTop(rect) + SliderHeight / 2.0;
                CenterDiamond(i, rect, _diamonds[i]);
            }
            InvalidateVisual();
        }

        // ===== Rectangle drag (moves slider) =====
        private void Slider_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border rect) return;
            if (!e.GetCurrentPoint(rect).Properties.IsLeftButtonPressed) return;
            if (_valueDragIndex >= 0) return; // ignore if diamond is being dragged

            _dragIndex = (int)rect.Tag!;
            e.Pointer.Capture(rect);

            var p = e.GetPosition(SliderCanvas);
            _dragOffsetY = p.Y - Canvas.GetTop(rect);

            e.Handled = true;
        }

        private void Slider_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_dragIndex < 0 || sender is not Border rect) return;

            var p = e.GetPosition(SliderCanvas);
            double newTop = p.Y - _dragOffsetY;
            newTop = Clamp(newTop, 0, Math.Max(0, SliderCanvas.Bounds.Height - SliderHeight));

            Canvas.SetTop(rect, newTop);

            VM.SliderCentersY[_dragIndex] = newTop + SliderHeight / 2.0;

            // keep diamond centered on rect center
            CenterDiamond(_dragIndex, rect, _diamonds[_dragIndex]);

            e.Handled = true;
        }

        private void Slider_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            e.Pointer.Capture(null);
            _dragIndex = -1;
            e.Handled = true;
            e.Handled = true;
        }

        // ===== Diamond drag (updates value; slider doesn't move) =====
        private void Diamond_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border diamond) return;
            if (!e.GetCurrentPoint(diamond).Properties.IsLeftButtonPressed) return;
            if (_dragIndex >= 0) return; // ignore if rect is being dragged

            _valueDragIndex = (int)diamond.Tag!;
            e.Pointer.Capture(diamond);

            var p = e.GetPosition(SliderCanvas);
            _valuePressY = p.Y;
            _valueBaseAtPress = VM.SliderValues[_valueDragIndex];

            e.Handled = true;
        }

        private void Diamond_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_valueDragIndex < 0) return;

            var p = e.GetPosition(SliderCanvas);
            double dy = p.Y - _valuePressY; // down = positive

            double newValue = _valueBaseAtPress + dy * ValueSensitivity;
            // clamp if needed: newValue = Clamp(newValue, 0, 100);

            VM.SliderValues[_valueDragIndex] = newValue;
            e.Handled = true;
        }

        private void Diamond_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            e.Pointer.Capture(null);
            _valueDragIndex = -1;
            e.Handled = true;
        }

        private static double Clamp(double v, double lo, double hi) =>
            v < lo ? lo : (v > hi ? hi : v);
    }
}