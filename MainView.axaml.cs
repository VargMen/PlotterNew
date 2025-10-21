using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using System;
using System.Reflection;

namespace PlotterNew
{
    public partial class MainView : Window
    {
        public MainView()
        {
            InitializeComponent();
        }

        private void PlotSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo)
            {
                if (MyCanvas == null)
                    return;

                int index = combo.SelectedIndex;
                var (scale, offset) = MyCanvas.GetWaveformParameters(index);
                ScaleSlider.Value = scale;
                OffsetSlider.Value = offset;
            }
        }
        private void SliderScale_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            // Cast sender to Slider
            if (sender is Slider slider)
            {
                var (scale, offset) = MyCanvas.GetWaveformParameters(PlotSelector.SelectedIndex);
                MyCanvas.UpdateWaveformParameters(PlotSelector.SelectedIndex, slider.Value, offset);
            }
        }
        private void SliderOffset_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            // Cast sender to Slider
            if (sender is Slider slider)
            {
                var (scale, offset) = MyCanvas.GetWaveformParameters(PlotSelector.SelectedIndex);
                MyCanvas.UpdateWaveformParameters(PlotSelector.SelectedIndex, scale, slider.Value);
            }
        }
    }
}