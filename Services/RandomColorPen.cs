using System;
using Avalonia.Media;
using System.Security.Cryptography;

namespace PlotterNew.Services
{
    public static class RandomColorPen
    {
        // Option A: single Random (fast; lock if you might hit it from multiple threads)
        private static readonly Random _rng = new Random();
        private static readonly object _lock = new();

        public static Pen NextPen(double thickness = 2.0)
            => new Pen(new SolidColorBrush(Color.Parse(RandomHexRgb())), thickness);

        public static string RandomHexRgb()
        {
            var rng = Random.Shared;
            int r = rng.Next(256), g = rng.Next(256), b = rng.Next(256);
            return $"#{r:X2}{g:X2}{b:X2}ff";
        }

        public static Color NextColor()
        {
            lock (_lock)
            {
                return Color.FromArgb(255,
                    (byte)_rng.Next(256),
                    (byte)_rng.Next(256),
                    (byte)_rng.Next(256));
            }
        }

        // Option B: cryptographic RNG (no lock needed, a bit heavier)
        public static Color NextColorCrypto()
        {
            Span<byte> b = stackalloc byte[3];
            RandomNumberGenerator.Fill(b);
            return Color.FromArgb(255, b[0], b[1], b[2]);
        }
    }
}
