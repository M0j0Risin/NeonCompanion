using NeonCompanion.Audio;

namespace NeonCompanion.Tests;

/// <summary>Counts wave-in devices once per assembly; CI runners have none.</summary>
internal static class AudioInputDevice
{
    public static readonly int InputCount;
    public static readonly string Unavailable;

    static AudioInputDevice()
    {
        try
        {
            InputCount = WinMmAudioCapture.InputDeviceCount();
            Unavailable = InputCount > 0 ? "" : "No wave-in device on this machine.";
        }
        catch (Exception ex)
        {
            InputCount = 0;
            Unavailable = "winmm probe failed: " + ex.Message;
        }
    }
}

/// <summary>Skips unless the machine has a microphone. A local check, never a CI safety net.</summary>
public sealed class AudioInputDeviceFactAttribute : FactAttribute
{
    public AudioInputDeviceFactAttribute()
    {
        if (AudioInputDevice.InputCount == 0)
        {
            Skip = AudioInputDevice.Unavailable;
        }
    }
}
