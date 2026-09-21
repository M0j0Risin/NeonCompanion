using System.Runtime.InteropServices;

namespace NeonCompanion.Audio;

/// <summary>
/// The WinMM wave-out and wave-in imports, shared by <see cref="WinMmAudioPlayback"/> and
/// <see cref="WinMmAudioCapture"/>.
///
/// <para><b>Every header parameter is an <see cref="IntPtr"/>.</b> NAudio's <c>WaveHeader</c> is
/// a class marshalled by value: under NativeAOT the prepared flag lands in a temporary native
/// copy and the next <c>waveOutWrite</c> sees an unprepared header. A signature taking
/// <c>ref WaveHdr</c> reintroduces that, works under the JIT and fails only once published.
/// Both structs here are blittable (<see cref="IntPtr"/>, <see cref="uint"/>, <see cref="ushort"/>
/// only), so the source-generated imports below marshal nothing.</para>
/// </summary>
internal static partial class WinMmNative
{
    public const int WaveMapper = -1;
    public const int CallbackNull = 0x00000000;
    public const int CallbackEvent = 0x00050000;
    public const uint WhdrDone = 0x00000001;
    public const ushort WaveFormatPcm = 1;

    public const int MmsyserrNoError = 0;
    public const int MmsyserrBadDeviceId = 2;
    public const int MmsyserrNoDriver = 6;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;

        public static WaveFormatEx Pcm(PcmFormat format)
        {
            var result = new WaveFormatEx
            {
                wFormatTag = WaveFormatPcm,
                nChannels = (ushort)format.Channels,
                nSamplesPerSec = (uint)format.SampleRate,
                wBitsPerSample = (ushort)format.BitsPerSample,
                nBlockAlign = (ushort)format.BlockAlign,
                cbSize = 0,
            };
            result.nAvgBytesPerSec = result.nSamplesPerSec * result.nBlockAlign;
            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WaveHdr
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [LibraryImport("winmm.dll")]
    public static partial uint waveOutGetNumDevs();

    [LibraryImport("winmm.dll")]
    public static partial int waveOutOpen(out IntPtr handle, int deviceId, ref WaveFormatEx format, IntPtr callback, IntPtr instance, int flags);

    [LibraryImport("winmm.dll")]
    public static partial int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);

    [LibraryImport("winmm.dll")]
    public static partial int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);

    [LibraryImport("winmm.dll")]
    public static partial int waveOutWrite(IntPtr handle, IntPtr header, int size);

    [LibraryImport("winmm.dll")]
    public static partial int waveOutReset(IntPtr handle);

    [LibraryImport("winmm.dll")]
    public static partial int waveOutClose(IntPtr handle);

    [LibraryImport("winmm.dll")]
    public static partial uint waveInGetNumDevs();

    [LibraryImport("winmm.dll")]
    public static partial int waveInOpen(out IntPtr handle, int deviceId, ref WaveFormatEx format, IntPtr callback, IntPtr instance, int flags);

    [LibraryImport("winmm.dll")]
    public static partial int waveInPrepareHeader(IntPtr handle, IntPtr header, int size);

    [LibraryImport("winmm.dll")]
    public static partial int waveInUnprepareHeader(IntPtr handle, IntPtr header, int size);

    [LibraryImport("winmm.dll")]
    public static partial int waveInAddBuffer(IntPtr handle, IntPtr header, int size);

    [LibraryImport("winmm.dll")]
    public static partial int waveInStart(IntPtr handle);

    [LibraryImport("winmm.dll")]
    public static partial int waveInReset(IntPtr handle);

    [LibraryImport("winmm.dll")]
    public static partial int waveInClose(IntPtr handle);
}
