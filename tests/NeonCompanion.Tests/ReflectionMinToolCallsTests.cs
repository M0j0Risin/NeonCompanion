using NeonCompanion.Diagnostics;
using NeonCompanion.Settings;
using NeonCompanion.Skills;

namespace NeonCompanion.Tests;

public class ReflectionMinToolCallsTests
{
    [Fact]
    public void Constants_ArePinned()
    {
        Assert.Equal(4, ReflectionMinToolCalls.Default);
        Assert.Equal(3, ReflectionMinToolCalls.Min);
        Assert.Equal(20, ReflectionMinToolCalls.Max);
        Assert.Equal(ReflectionMinToolCalls.Default, new AppSettingsData().ReflectionMinToolCalls);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(20)]
    public void Resolve_ReadsAValueInRange(int calls)
    {
        Assert.Equal(calls, ReflectionMinToolCalls.Resolve(new AppSettingsData { ReflectionMinToolCalls = calls }));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(21)]
    [InlineData(0)]
    public void Resolve_OutOfRange_WarnsAndUsesTheDefault(int calls)
    {
        var warnings = new List<DiagnosticEvent>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == SkillCatalog.Category && e.Level == DiagnosticLevel.Warning) warnings.Add(e); };
        DiagnosticLog.Emitted += capture;
        try
        {
            Assert.Equal(4, ReflectionMinToolCalls.Resolve(new AppSettingsData { ReflectionMinToolCalls = calls }));
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        var warning = Assert.Single(warnings);
        Assert.Equal($"ReflectionMinToolCalls={calls} is not 3 to 20. Using 4.", warning.Message);
    }
}
