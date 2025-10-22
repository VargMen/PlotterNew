using PlotterNew.Core.Interfaces;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace PlotterNew.Core.Services
{
    public sealed class FakeSineFeed : IDataFeed
    {
        private readonly int _seriesCount;
        private readonly double _sampleRateHz;
        private readonly int _batchSize;

        private readonly object _gate = new();
        private CancellationTokenSource? _cts;
        private Task? _worker;
        private Stopwatch? _sw;

        public event Action<DataBatch>? BatchReady;

        public FakeSineFeed(int seriesCount = 2, double sampleRateHz = 200.0, int batchSize = 8)
        {
            if (seriesCount <= 0) throw new ArgumentOutOfRangeException(nameof(seriesCount));
            if (sampleRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));

            _seriesCount = seriesCount;
            _sampleRateHz = sampleRateHz;
            _batchSize = batchSize;
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_worker is not null && !_worker.IsCompleted)
                    return; // already running

                _cts = new CancellationTokenSource();
                _sw = Stopwatch.StartNew();

                _worker = Task.Run(() => Run(_cts.Token, _sw), _cts.Token);
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                _cts?.Cancel();
            }

            try { _worker?.Wait(250); } catch { /* ignore */ }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        private void Run(CancellationToken ct, Stopwatch sw)
        {
            double dt = 1.0 / _sampleRateHz;
            double nextSampleT = 0.0;

            var batch = new DataBatch(_seriesCount, _batchSize);

            const int MinSleepMs = 1;

            while (!ct.IsCancellationRequested)
            {
                double now = sw.Elapsed.TotalSeconds;

                if (now + 1e-9 < nextSampleT)
                {
                    Thread.Sleep(MinSleepMs);
                    continue;
                }

                int producedThisLoop = 0;
                while (now + 1e-9 >= nextSampleT && !ct.IsCancellationRequested)
                {
                    double t = nextSampleT;
                    batch.X.Add(t);

                    for (int s = 0; s < _seriesCount; s++)
                    {
                        // Different phase for each series
                        double y = System.Math.Sin(2 * System.Math.PI * 1.0 * t + s * System.Math.PI / 3.0);
                        batch.Y[s].Add(y);
                    }

                    producedThisLoop++;
                    nextSampleT += dt;

                    if (batch.X.Count >= _batchSize)
                    {
                        try { BatchReady?.Invoke(batch); }
                        catch { /* never crash the feed */ }

                        batch = new DataBatch(_seriesCount, _batchSize);
                    }

                    if (producedThisLoop >= _batchSize * 4)
                        break;
                }

                Thread.Sleep(MinSleepMs);
            }
        }
    }
}
