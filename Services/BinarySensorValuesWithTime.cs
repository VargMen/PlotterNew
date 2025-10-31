using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlotterNew.Services
{
    public sealed record SamplePacket(ushort Seq, double TimeSec, double[] Values);

    public sealed class SerialReader32 : IDisposable
    {
        private readonly SerialPort _port;
        private readonly byte[] _buf = new byte[8192];
        private int _len = 0;
        private CancellationTokenSource? _cts;
        private Task? _task;

        public readonly ConcurrentQueue<SamplePacket> Queue = new();

        private const int FrameSize = 32;
        private const byte M0 = 0xAA, M1 = 0x55;

        // unwrapping state
        private ulong _wrapBase = 0;            // multiples of 2^32 us
        private uint _lastMicros = 0;
        private bool _haveLast = false;

        public SerialReader32(string port, int baud = 230400)
        {
            _port = new SerialPort(port, baud) { ReadTimeout = 500, WriteTimeout = 500 };
        }

        public void Start() { _port.Open(); _cts = new(); _task = Task.Run(() => Loop(_cts.Token)); }
        public void Stop() { _cts?.Cancel(); try { _task?.Wait(); } catch { } if (_port.IsOpen) _port.Close(); }
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

                Buffer.BlockCopy(rx, 0, _buf, _len, n);
                _len += n;

                int i = 0;
                while (_len - i >= FrameSize)
                {
                    if (!(_buf[i] == M0 && _buf[i + 1] == M1)) { i++; continue; }
                    if (_len - i < FrameSize) break;

                    // CRC
                    ushort crcCalc = Crc16(_buf, i, FrameSize - 2);
                    ushort crcFrm = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(_buf, i + FrameSize - 2, 2));
                    if (crcCalc != crcFrm) { i++; continue; }

                    byte ver = _buf[i + 2], ch = _buf[i + 3];
                    if (ver != 1 || ch != 10) { i += 2; continue; }

                    ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(_buf, i + 4, 2));
                    uint tUs = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(_buf, i + 6, 4));

                    // unwrap micros() to 64-bit, then convert to seconds
                    if (_haveLast && tUs < _lastMicros) _wrapBase += 1UL << 32; // wrap detected
                    _lastMicros = tUs; _haveLast = true;
                    double timeSec = (_wrapBase + tUs) * 1e-6;

                    double[] vals = new double[10];
                    int off = i + 10;
                    for (int k = 0; k < 10; k++)
                    {
                        short raw = BinaryPrimitives.ReadInt16LittleEndian(new ReadOnlySpan<byte>(_buf, off + 2 * k, 2));
                        vals[k] = raw / 10000.0;
                    }

                    Queue.Enqueue(new SamplePacket(seq, timeSec, vals));
                    i += FrameSize;
                }

                if (i > 0)
                {
                    int rem = _len - i;
                    Buffer.BlockCopy(_buf, i, _buf, 0, rem);
                    _len = rem;
                }
            }
        }

        // CCITT-FALSE
        private static ushort Crc16(byte[] d, int s, int len, ushort crc = 0xFFFF)
        {
            for (int i = 0; i < len; i++)
            {
                crc ^= (ushort)(d[s + i] << 8);
                for (int b = 0; b < 8; b++)
                    crc = (ushort)(((crc & 0x8000) != 0) ? ((crc << 1) ^ 0x1021) : (crc << 1));
            }
            return crc;
        }
    }

}
