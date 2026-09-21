using System.Runtime.InteropServices;
using NeonCompanion.Diagnostics;
using static NeonCompanion.Audio.WinMmNative;

namespace NeonCompanion.Audio;

/// <summary>
/// Speaker output over WinMM, written to survive NativeAOT.
///
/// <para>NAudio's <c>WaveOutEvent</c> stops with <c>WaveHeaderUnprepared calling waveOutWrite</c>
/// in a published binary: its <c>WaveHeader</c> is a <em>class</em> passed by value, so the
/// marshaller hands WinMM a temporary native copy, the prepared flag is written into that copy
/// and discarded, and the next write presents an unprepared header. Measured against the
/// published binary — 24000 bytes queued, <b>4800 drained</b>, then dead: one buffer of about a
/// tenth of a second, which is why speech output appeared to work and was silent.</para>
///
/// <para>So: <b>every header lives in unmanaged memory this class owns and is handed to WinMM as
/// an <see cref="IntPtr"/></b>; this class reads and writes it through a typed pointer. Nothing is
/// marshalled by value. Only the published build shows the failure, which is why
/// <c>--audio-check</c> exists.</para>
///
/// <para>Callers push PCM into a managed queue; a pump thread moves it into four 100 ms device
/// buffers as they come free. <see cref="BufferedBytes"/> counts bytes until the device reports a
/// buffer <em>done</em>, so zero means "heard", not "handed over" — the end-of-turn wait and the
/// audio check both rely on that.</para>
/// </summary>
public sealed unsafe class WinMmAudioPlayback : IAudioPlayback
{
    private const string Category = "WinMmPlayback";

    /// <summary>
    /// Buffers in flight. Four at 100 ms covers the gaps between synthesized sentences without
    /// adding latency anyone can hear at the start of a reply.
    /// </summary>
    private const int BufferCount = 4;

    private readonly int _bufferBytes;
    private readonly object _gate = new();
    private readonly Queue<byte[]> _pending = new();

    private IntPtr _device;
    private IntPtr[] _headers = Array.Empty<IntPtr>();
    private bool[] _headerBusy = Array.Empty<bool>();
    private int[] _headerBytes = Array.Empty<int>();
    private int _headOffset;
    private AutoResetEvent? _bufferFree;
    private Thread? _pump;

    private volatile bool _running;
    private long _queuedBytes;
    private long _inFlightBytes;
    private bool _disposed;

    public WinMmAudioPlayback(PcmFormat format, int bufferMilliseconds = 100)
    {
        if (format.SampleRate <= 0 || format.Channels <= 0 || format.BitsPerSample is not (8 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "WinMM playback needs a positive rate, 8 or 16 bits and at least one channel.");
        }

        if (bufferMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferMilliseconds));
        }

        Format = format;
        _bufferBytes = Math.Max(format.BlockAlign, format.BytesFor(bufferMilliseconds));
    }

    public PcmFormat Format { get; }

    public bool IsPlaying => _running;

    public long BufferedBytes => Interlocked.Read(ref _queuedBytes) + Interlocked.Read(ref _inFlightBytes);

    /// <summary>How many wave-out devices Windows reports. Zero on a headless CI runner.</summary>
    public static int OutputDeviceCount() => (int)waveOutGetNumDevs();

    /// <summary>
    /// Opens and immediately closes <c>WAVE_MAPPER</c> at <paramref name="format"/> without playing
    /// anything. The <c>--smoke</c> check: proves the imports bind and the open path round-trips
    /// in the published binary. Returns the MMSYSERR code (0 = opened).
    /// </summary>
    public static int ProbeDefaultDevice(PcmFormat format)
    {
        var wave = WaveFormatEx.Pcm(format);
        int result = waveOutOpen(out var handle, WaveMapper, ref wave, IntPtr.Zero, IntPtr.Zero, CallbackNull);
        if (result == MmsyserrNoError && handle != IntPtr.Zero)
        {
            waveOutClose(handle);
        }

        return result;
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running)
            {
                return;
            }

            var wave = WaveFormatEx.Pcm(Format);
            _bufferFree = new AutoResetEvent(false);

            int result = waveOutOpen(
                out _device,
                WaveMapper,
                ref wave,
                _bufferFree.SafeWaitHandle.DangerousGetHandle(),
                IntPtr.Zero,
                CallbackEvent);

            if (result != MmsyserrNoError)
            {
                _bufferFree.Dispose();
                _bufferFree = null;
                _device = IntPtr.Zero;
                throw new InvalidOperationException($"waveOutOpen failed with MMSYSERR {result}.");
            }

            _headers = new IntPtr[BufferCount];
            _headerBusy = new bool[BufferCount];
            _headerBytes = new int[BufferCount];

            for (int i = 0; i < BufferCount; i++)
            {
                var data = Marshal.AllocHGlobal(_bufferBytes);
                var header = (WaveHdr*)Marshal.AllocHGlobal(sizeof(WaveHdr));
                *header = default;
                header->lpData = data;
                header->dwBufferLength = (uint)_bufferBytes;
                _headers[i] = (IntPtr)header;
            }

            _headOffset = 0;
            _running = true;
            _pump = new Thread(Pump)
            {
                IsBackground = true,
                Name = "NeonCompanion WinMM playback",
                Priority = ThreadPriority.AboveNormal,
            };
            _pump.Start();
        }
    }

    public void Write(byte[] pcm, int count)
    {
        if (pcm is null || count <= 0)
        {
            return;
        }

        count = Math.Min(count, pcm.Length);
        var copy = new byte[count];
        Buffer.BlockCopy(pcm, 0, copy, 0, count);

        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _pending.Enqueue(copy);
            Interlocked.Add(ref _queuedBytes, count);
        }

        _bufferFree?.Set();
    }

    /// <summary>
    /// Drops everything queued and silences the device immediately. <c>waveOutReset</c> returns
    /// the in-flight buffers rather than letting them finish, so speech stops mid-word instead of
    /// a beat later; they are reclaimed here, so <see cref="BufferedBytes"/> is zero on return.
    /// </summary>
    public void ClearBuffer()
    {
        lock (_gate)
        {
            _pending.Clear();
            _headOffset = 0;
            Interlocked.Exchange(ref _queuedBytes, 0);

            if (_device != IntPtr.Zero)
            {
                waveOutReset(_device);
                ReclaimFinished(force: true);
            }
        }
    }

    public void Stop()
    {
        Thread? pump;

        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            pump = _pump;
            _pump = null;

            _pending.Clear();
            _headOffset = 0;
            Interlocked.Exchange(ref _queuedBytes, 0);
        }

        _bufferFree?.Set();
        pump?.Join(TimeSpan.FromSeconds(2));

        lock (_gate)
        {
            if (_device == IntPtr.Zero)
            {
                return;
            }

            // Reset before unpreparing: it returns every queued buffer, so unpreparing cannot race
            // the device still reading one.
            waveOutReset(_device);

            for (int i = 0; i < _headers.Length; i++)
            {
                if (_headers[i] == IntPtr.Zero)
                {
                    continue;
                }

                var header = (WaveHdr*)_headers[i];
                waveOutUnprepareHeader(_device, _headers[i], sizeof(WaveHdr));
                if (header->lpData != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(header->lpData);
                }

                Marshal.FreeHGlobal(_headers[i]);
            }

            _headers = Array.Empty<IntPtr>();
            _headerBusy = Array.Empty<bool>();
            _headerBytes = Array.Empty<int>();
            Interlocked.Exchange(ref _inFlightBytes, 0);

            waveOutClose(_device);
            _device = IntPtr.Zero;

            _bufferFree?.Dispose();
            _bufferFree = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    /// <summary>Unprepares every busy header the device has finished with (or all of them after a reset). Caller holds the gate.</summary>
    private void ReclaimFinished(bool force)
    {
        for (int i = 0; i < _headers.Length; i++)
        {
            if (!_headerBusy[i])
            {
                continue;
            }

            var header = (WaveHdr*)_headers[i];
            if (!force && (header->dwFlags & WhdrDone) == 0)
            {
                continue;
            }

            waveOutUnprepareHeader(_device, _headers[i], sizeof(WaveHdr));
            _headerBusy[i] = false;
            Interlocked.Add(ref _inFlightBytes, -_headerBytes[i]);
            _headerBytes[i] = 0;
        }
    }

    /// <summary>
    /// Moves queued PCM into free device buffers and reclaims finished ones. The wait is bounded
    /// rather than indefinite: <see cref="Stop"/> sets the event, but a device that stops
    /// returning buffers would otherwise park this thread with no way to notice shutdown.
    /// </summary>
    private void Pump()
    {
        while (_running)
        {
            try
            {
                _bufferFree?.WaitOne(50);

                lock (_gate)
                {
                    if (!_running || _device == IntPtr.Zero)
                    {
                        continue;
                    }

                    ReclaimFinished(force: false);

                    for (int i = 0; i < _headers.Length && _pending.Count > 0; i++)
                    {
                        if (_headerBusy[i])
                        {
                            continue;
                        }

                        var header = (WaveHdr*)_headers[i];
                        var chunk = _pending.Peek();
                        int length = Math.Min(chunk.Length - _headOffset, _bufferBytes);

                        Marshal.Copy(chunk, _headOffset, header->lpData, length);
                        header->dwBufferLength = (uint)length;
                        header->dwBytesRecorded = 0;
                        header->dwFlags = 0;

                        waveOutPrepareHeader(_device, _headers[i], sizeof(WaveHdr));
                        int result = waveOutWrite(_device, _headers[i], sizeof(WaveHdr));
                        if (result != MmsyserrNoError)
                        {
                            waveOutUnprepareHeader(_device, _headers[i], sizeof(WaveHdr));
                            DiagnosticLog.Error(Category, $"waveOutWrite failed with MMSYSERR {result}.");
                            continue;
                        }

                        _headerBusy[i] = true;
                        _headerBytes[i] = length;

                        // In flight before it leaves the queue, so BufferedBytes never dips to zero
                        // between the two counters while audio is still owed.
                        Interlocked.Add(ref _inFlightBytes, length);
                        Interlocked.Add(ref _queuedBytes, -length);

                        // A chunk longer than one buffer stays at the head; the offset walks it.
                        // Splitting rather than dropping is what keeps a long sentence intact.
                        if (_headOffset + length >= chunk.Length)
                        {
                            _pending.Dequeue();
                            _headOffset = 0;
                        }
                        else
                        {
                            _headOffset += length;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error(Category, $"Playback pump failed: {ex.Message}", ex);
                return;
            }
        }
    }
}
