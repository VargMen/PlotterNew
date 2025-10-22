using Avalonia.Media;
using System.Collections.Generic;

namespace PlotterNew.Core.Models
{
    public class Series
    {
        const int DefaultCapacity = 60000;
        public List<double> YCoords { get; set; } = new List<double>(DefaultCapacity);
        public List<int> DecimatedIndices { get; } = new();
        public double YOffset { get; set; } = 0.0;
        public double YScale { get; set; } = 1.0;
        public Pen Pen { get; set; } = new Pen(Brushes.White, 1.0);
        public double CalcTransformedY(int index) => YOffset + YScale* YCoords[index];
        public int Count => YCoords.Count;
    }
}
