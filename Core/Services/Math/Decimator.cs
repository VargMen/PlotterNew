using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlotterNew.Core.Services.Math
{
    public static class Decimator
    {
        public static void BucketMinMax(
            List<double> xs, List<double> ys,
            int i0, int i1, int target,
            List<int> outIndices)
        {
            outIndices.Clear();
            if (i1 <= i0) return;

            int count = i1 - i0 + 1;
            if (count <= target)
            {
                for (int i = i0; i <= i1; i++) outIndices.Add(i);
                return;
            }

            int buckets = System.Math.Min(target / 2, count);
            int bucketSize = System.Math.Max(1, count / buckets);

            for (int b = 0; b < buckets; b++)
            {
                int a = i0 + b * bucketSize;
                int z = System.Math.Min(i1, a + bucketSize - 1);

                int minI = a, maxI = a;
                double minY = ys[a], maxY = ys[a];

                for (int i = a + 1; i <= z; i++)
                {
                    double y = ys[i];
                    if (y < minY) { minY = y; minI = i; }
                    if (y > maxY) { maxY = y; maxI = i; }
                }

                if (xs[minI] <= xs[maxI]) { outIndices.Add(minI); outIndices.Add(maxI); }
                else { outIndices.Add(maxI); outIndices.Add(minI); }
            }
        }
    }
}
