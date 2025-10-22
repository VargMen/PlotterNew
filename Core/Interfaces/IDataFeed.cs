using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlotterNew.Core.Interfaces
{
    public interface IDataFeed : IDisposable
    {
        void Start();
        void Stop();

        event Action<DataBatch> BatchReady;
    }

    public sealed class DataBatch
    {
        // Shared X for all series
        public List<double> X { get; } = new();
        public List<List<double>> Y { get; } = new();
        public DataBatch(int seriesCount, int capacityPerSeries = 0)
        {
            for (int i = 0; i < seriesCount; i++)
                Y.Add(capacityPerSeries > 0 ? new List<double>(capacityPerSeries) : new List<double>());
        }
    }
}
