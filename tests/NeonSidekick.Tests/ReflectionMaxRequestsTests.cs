using NeonSidekick.Diagnostics;
using NeonSidekick.Settings;
using NeonSidekick.Skills;

namespace NeonSidekick.Tests;

public class ReflectionMaxRequestsTests
{
    [Fact]
    public void Constants_ArePinned()
    {
        Assert.Equal(4, ReflectionMaxRequests.Default);
        Assert.Equal(1, ReflectionMaxRequests.Min);
        Assert.Equal(20, ReflectionMaxRequests.Max);
        Assert.Equal(ReflectionMaxRequests.Default, new AppSettingsData().ReflectionMaxRequests);
        Assert.Equal(ReflectionMaxRequests.Default, SkillLearner.DefaultMaxRequests);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(20)]
    public void Resolve_ReadsAValueInRange(int requests)
    {
        Assert.Equal(requests, ReflectionMaxRequests.Resolve(new AppSettingsData { ReflectionMaxRequests = requests }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-1)]
    public void Resolve_OutOfRange_WarnsAndUsesTheDefault(int requests)
    {
        var warnings = new List<DiagnosticEvent>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == SkillCatalog.Category && e.Level == DiagnosticLevel.Warning) warnings.Add(e); };
        DiagnosticLog.Emitted += capture;
        try
        {
            Assert.Equal(4, ReflectionMaxRequests.Resolve(new AppSettingsData { ReflectionMaxRequests = requests }));
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        var warning = Assert.Single(warnings);
        Assert.Equal($"ReflectionMaxRequests={requests} is not 1 to 20. Using 4.", warning.Message);
    }
}
