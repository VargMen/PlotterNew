using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace PlotterNew.Services
{
    public record TenVals(double[] Values);

    public sealed class SerialTenReader : IDisposable
    {
        private readonly SerialPort _port;
        private readonly byte[] _buf = new byte[4096];
        private int _len = 0;
        private CancellationTokenSource? _cts;
        private Task? _task;

        public readonly ConcurrentQueue<TenVals> Queue = new();

        private const int FrameSize = 26;
        private const byte M0 = 0xAA, M1 = 0x55;

        public SerialTenReader(string port, int baud = 230400)
        {
            _port = new SerialPort(port, baud) { ReadTimeout = 500, WriteTimeout = 500 };
        }

        public void Start() { _port.Open(); _cts = new(); _task = Task.Run(() => Loop(_cts.Token)); }
        public void Stop() { _cts?.Cancel(); _task?.Wait(); _port.Close(); }
        public void Dispose() => Stop();

        private async Task Loop(CancellationToken ct)
        {
            var rx = new byte[1024];
            while (!ct.IsCancellationRequested)
            {
                int n; try { n = await _port.BaseStream.ReadAsync(rx, 0, rx.Length, ct); }
                catch (OperationCanceledException) { break; }
                catch { continue; }
                if (n <= 0) continue;

                // append
                Buffer.BlockCopy(rx, 0, _buf, _len, n);
                _len += n;

                // parse
                int i = 0;
                while (_len - i >= FrameSize)
                {
                    if (!(_buf[i] == M0 && _buf[i + 1] == M1)) { i++; continue; }
                    if (_len - i < FrameSize) break;

                    // CRC check
                    ushort crcCalc = Crc16(_buf, i, FrameSize - 2);
                    ushort crcFrm = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(_buf, i + FrameSize - 2, 2));
                    if (crcCalc != crcFrm) { i++; continue; }

                    // quick header sanity
                    byte ver = _buf[i + 2], ch = _buf[i + 3];
                    if (ver != 1 || ch != 10) { i += 2; continue; }

                    double[] vals = new double[10];
                    int off = i + 4;
                    for (int k = 0; k < 10; k++)
                    {
                        short raw = BinaryPrimitives.ReadInt16LittleEndian(new ReadOnlySpan<byte>(_buf, off + 2 * k, 2));
                        vals[k] = raw / 10000.0;
                    }
                    Queue.Enqueue(new TenVals(vals));
                    i += FrameSize;
                }

                // compact
                if (i > 0)
                {
                    int rem = _len - i;
                    Buffer.BlockCopy(_buf, i, _buf, 0, rem);
                    _len = rem;
                }
            }
        }

        private static ushort Crc16(byte[] d, int s, int len, ushort crc = 0xFFFF)
        {
            for (int i = 0; i < len; i++)
            {
                crc ^= (ushort)(d[s + i] << 8);
                for (int b = 0; b < 8; b++) crc = (ushort)(((crc & 0x8000) != 0) ? ((crc << 1) ^ 0x1021) : (crc << 1));
            }
            return crc;
        }
    }

}
