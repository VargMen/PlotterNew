using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlotterNew.Core.Models
{
    public class PlotModel
    {
        public List<Series> SeriesList { get; set; } = new List<Series>();
        public List<double> TimeSeries = new List<double>();
        public double XMin { get; set; } = 0.0;
        public double XMax { get; set; } = 150.0;
        public double YMin { get; set; } = 0.0;
        public double YMax { get; set; } = 0.0;
        public (int i0, int i1) GetVisibleRangeIndices()
        {
            if (TimeSeries.Count == 0) return (0, -1);
            int i0 = LowerBound(TimeSeries, XMin);
            int i1 = UpperBound(TimeSeries, XMax);
            i0 = Math.Clamp(i0, 0, TimeSeries.Count - 1);
            i1 = Math.Clamp(i1, 0, TimeSeries.Count - 1);
            //if (i1 < i0) (i0, i1) = (i1, i0);

            return (i0, i1);
        }

        private static int LowerBound(List<double> a, double x)
        {
            int lo = 0, hi = a.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (a[mid] < x) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private static int UpperBound(List<double> a, double x)
        {
            int lo = 0, hi = a.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (a[mid] <= x) lo = mid + 1;
                else hi = mid;
            }
            return lo - 1;
        }
        public void Pan(double dxData, double dyData) { XMin += dxData; XMax += dxData; YMin += dyData; YMax += dyData; }
    }
}
