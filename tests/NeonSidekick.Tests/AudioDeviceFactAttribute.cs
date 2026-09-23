using NeonSidekick.Audio;

namespace NeonSidekick.Tests;

/// <summary>Counts wave-out devices once per assembly; CI runners have none.</summary>
internal static class AudioDevice
{
    public static readonly int OutputCount;
    public static readonly string Unavailable;

    static AudioDevice()
    {
        try
        {
            OutputCount = WinMmAudioPlayback.OutputDeviceCount();
            Unavailable = OutputCount > 0 ? "" : "No wave-out device on this machine.";
        }
        catch (Exception ex)
        {
            OutputCount = 0;
            Unavailable = "winmm probe failed: " + ex.Message;
        }
    }
}

/// <summary>Skips unless the machine has an audio output device. A local check, never a CI safety net.</summary>
public sealed class AudioDeviceFactAttribute : FactAttribute
{
    public AudioDeviceFactAttribute()
    {
        if (AudioDevice.OutputCount == 0)
        {
            Skip = AudioDevice.Unavailable;
        }
    }
}
