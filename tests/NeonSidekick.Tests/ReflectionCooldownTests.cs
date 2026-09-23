using NeonSidekick.Diagnostics;
using NeonSidekick.Settings;
using NeonSidekick.Skills;

namespace NeonSidekick.Tests;

public class ReflectionCooldownTests
{
    [Fact]
    public void Constants_ArePinned()
    {
        Assert.Equal(5, ReflectionCooldown.Default);   // 30 for an hour on 2026-09-19
        Assert.Equal(0, ReflectionCooldown.Min);
        Assert.Equal(1440, ReflectionCooldown.Max);
        Assert.Equal(ReflectionCooldown.Default, new AppSettingsData().ReflectionCooldownMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(1440)]
    public void Resolve_ReadsAValueInRange_AsMinutes(int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), ReflectionCooldown.Resolve(new AppSettingsData { ReflectionCooldownMinutes = minutes }));
    }

    [Theory]
    [InlineData(1441)]
    [InlineData(-1)]
    public void Resolve_OutOfRange_WarnsAndUsesTheDefault(int minutes)
    {
        var warnings = new List<DiagnosticEvent>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == SkillCatalog.Category && e.Level == DiagnosticLevel.Warning) warnings.Add(e); };
        DiagnosticLog.Emitted += capture;
        try
        {
            Assert.Equal(TimeSpan.FromMinutes(5), ReflectionCooldown.Resolve(new AppSettingsData { ReflectionCooldownMinutes = minutes }));
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        var warning = Assert.Single(warnings);
        Assert.Equal($"ReflectionCooldownMinutes={minutes} is not 0 to 1440. Using 5.", warning.Message);
    }
}
