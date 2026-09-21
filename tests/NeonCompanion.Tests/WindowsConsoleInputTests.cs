using NeonCompanion.UI;
using static NeonCompanion.UI.ConsoleInputNative;

namespace NeonCompanion.Tests;

/// <summary>The console mode bits alone: the reader itself needs a real console handle.</summary>
public class WindowsConsoleInputTests
{
    private const uint ShellMode = EnableProcessedInput | EnableQuickEditMode | EnableVirtualTerminalInput | 0x0002 /* line input */;

    [Fact]
    public void Mode_DropsProcessedInput_InBothStates_SoCtrlCIsAKey()
    {
        uint released = WindowsConsoleInput.Mode(ShellMode, captured: false);
        uint captured = WindowsConsoleInput.Mode(ShellMode, captured: true);

        Assert.Equal(0u, released & EnableProcessedInput);
        Assert.Equal(0u, captured & EnableProcessedInput);
        Assert.Equal(0u, released & EnableVirtualTerminalInput);
        Assert.NotEqual(0u, released & EnableExtendedFlags);
        Assert.NotEqual(0u, released & EnableQuickEditMode);   // as the shell had it
        Assert.NotEqual(0u, captured & EnableMouseInput);
        Assert.Equal(0u, captured & EnableQuickEditMode);
        Assert.NotEqual(0u, captured & 0x0002);               // the rest untouched
    }

    [Fact]
    public void Released_KeepsProcessedInput_TheShellsCtrlCAgain()
    {
        uint released = WindowsConsoleInput.Released(ShellMode);

        Assert.NotEqual(0u, released & EnableProcessedInput);
        Assert.Equal(0u, released & EnableVirtualTerminalInput);
        Assert.NotEqual(0u, released & EnableExtendedFlags);
        Assert.Equal(0u, released & EnableMouseInput);
    }
}
