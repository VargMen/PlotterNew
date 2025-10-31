using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlotterNew.Services
{
    public class SineGenerator
    {
        private readonly Random _rng = new();

        // Parameters for one sine wave
        public double Y0 { get; private set; }      // vertical offset
        public double Amplitude { get; private set; }
        public double Omega { get; private set; }   // angular frequency (rad/s)
        public double Phase { get; private set; }   // phase shift (radians)
        public double TimeStep { get; private set; } // horizontal scaling

        Random rand = new Random();

        public SineGenerator(
            double? y0 = null,
            double? amplitude = null,
            double? frequencyHz = null,
            double? phase = null)
        {
            // If any argument is null, generate random parameter
            Y0 = y0 ?? 0;
            Amplitude = amplitude ?? NextRange(20, 50);
            double f = frequencyHz ?? NextRange(0.2, 0.3);   // Hz
            Omega = NextRange(2, 10);
            Phase = phase ?? NextRange(0, 2 * Math.PI);
        }
        public Avalonia.Point GetPoint(double t)
        {
            double x = t;
            double y = Y0 + Amplitude * Math.Sin(Omega * x + Phase);
            return new Avalonia.Point(x, y);
        }

        public Avalonia.Point GetRandomAmplitudePoint(double t)
        {
            double x = t;
            double y = Y0 + rand.NextDouble() * Amplitude * Math.Sin(Omega * x + Phase);
            return new Avalonia.Point(x, y);
        }

        public List<Avalonia.Point> Generate(double durationSeconds, double sampleRateHz)
        {
            int samples = (int)Math.Round(durationSeconds * sampleRateHz);
            var points = new List<Avalonia.Point>(samples);

            for (int n = 0; n < samples; ++n)
            {
                double t = n / sampleRateHz;
                points.Add(GetPoint(t));
            }

            return points;
        }
        public static List<SineGenerator> CreateMultiple(int count)
        {
            var list = new List<SineGenerator>(count);
            for (int i = 0; i < count; ++i)
                list.Add(new SineGenerator());
            return list;
        }

        private double NextRange(double min, double max) => min + _rng.NextDouble() * (max - min);
    }
}
