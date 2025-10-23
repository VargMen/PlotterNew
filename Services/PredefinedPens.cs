using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlotterNew.Services
{
    public static class PredefinedPens
    {
        public static readonly List<Pen> All = new()
        {
            new Pen(new SolidColorBrush(Colors.Yellow), 2),
            new Pen(new SolidColorBrush(Colors.Cyan), 2),
            new Pen(new SolidColorBrush(Colors.Blue), 2),
            new Pen(new SolidColorBrush(Colors.Red), 2),
            new Pen(new SolidColorBrush(Colors.Orange), 2),
            new Pen(new SolidColorBrush(Colors.Purple), 2),
            new Pen(new SolidColorBrush(Colors.Green), 2),
            new Pen(new SolidColorBrush(Colors.Magenta), 2),
            new Pen(new SolidColorBrush(Colors.Lime), 2),
            new Pen(new SolidColorBrush(Colors.DeepSkyBlue), 2)
        };

        public static readonly List<SolidColorBrush> Brushes = new()
        {
            new SolidColorBrush(Colors.Yellow),
            new SolidColorBrush(Colors.Cyan),
            new SolidColorBrush(Colors.Blue),
            new SolidColorBrush(Colors.Red),
            new SolidColorBrush(Colors.Orange),
            new SolidColorBrush(Colors.Purple),
            new SolidColorBrush(Colors.Green),
            new SolidColorBrush(Colors.Magenta),
            new SolidColorBrush(Colors.Lime),
            new SolidColorBrush(Colors.DeepSkyBlue),
        };

        // Optional helper to get one by index safely:
        public static Pen Get(int index)
            => All[index % All.Count];
    }
}
