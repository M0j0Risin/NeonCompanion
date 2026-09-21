using NeonCompanion.Diagnostics;
using NeonCompanion.Settings;
using NeonCompanion.Skills;

namespace NeonCompanion.Tests;

public class ReflectionWindowTests
{
    [Fact]
    public void Constants_ArePinned()
    {
        Assert.Equal(3, ReflectionWindow.Default);
        Assert.Equal(1, ReflectionWindow.Min);
        Assert.Equal(5, ReflectionWindow.Max);
        Assert.Equal(ReflectionWindow.Default, new AppSettingsData().ReflectionWindow);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Resolve_ReadsAValueInRange(int window)
    {
        Assert.Equal(window, ReflectionWindow.Resolve(new AppSettingsData { ReflectionWindow = window }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-2)]
    public void Resolve_OutOfRange_WarnsAndUsesTheDefault(int window)
    {
        var warnings = new List<DiagnosticEvent>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == SkillCatalog.Category && e.Level == DiagnosticLevel.Warning) warnings.Add(e); };
        DiagnosticLog.Emitted += capture;
        try
        {
            Assert.Equal(3, ReflectionWindow.Resolve(new AppSettingsData { ReflectionWindow = window }));
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        var warning = Assert.Single(warnings);
        Assert.Equal($"ReflectionWindow={window} is not 1 to 5. Using 3.", warning.Message);
    }
}
