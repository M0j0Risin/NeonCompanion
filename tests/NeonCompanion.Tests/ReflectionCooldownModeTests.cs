using NeonCompanion.Diagnostics;
using NeonCompanion.Settings;
using NeonCompanion.Skills;

namespace NeonCompanion.Tests;

public class ReflectionCooldownModeTests
{
    [Fact]
    public void Names_Default_AndDescriptions_ArePinned()
    {
        Assert.Equal(["all-skills", "last-written-skill"], ReflectionCooldownMode.Names);
        Assert.Equal("last-written-skill", ReflectionCooldownMode.Default);
        Assert.Equal(ReflectionCooldownMode.Default, new AppSettingsData().ReflectionCooldownMode);
        Assert.Equal("after any skill is written, every automatic reflection waits out the cooldown", ReflectionCooldownMode.Describe("all-skills"));
        Assert.Equal("only a turn that loaded the skill just written waits; another lesson reflects at once", ReflectionCooldownMode.Describe("last-written-skill"));
        Assert.Equal("", ReflectionCooldownMode.Describe("x"));
        Assert.Equal("all-skills", ReflectionCooldownMode.Name(ReflectionCooldownScope.AllSkills));
        Assert.Equal("last-written-skill", ReflectionCooldownMode.Name(ReflectionCooldownScope.LastWrittenSkill));
    }

    [Theory]
    [InlineData("all-skills", ReflectionCooldownScope.AllSkills, true)]
    [InlineData(" Last-Written-Skill ", ReflectionCooldownScope.LastWrittenSkill, true)]
    [InlineData("everything", ReflectionCooldownScope.LastWrittenSkill, false)]
    [InlineData(null, ReflectionCooldownScope.LastWrittenSkill, false)]
    public void TryParse_TrimsAndIgnoresCase(string? text, ReflectionCooldownScope expected, bool parsed)
    {
        Assert.Equal(parsed, ReflectionCooldownMode.TryParse(text, out var scope));
        Assert.Equal(expected, scope);
    }

    [Fact]
    public void Resolve_ReadsTheSavedWord_AndAnUnknownOne_WarnsAndUsesTheDefault()
    {
        Assert.Equal(ReflectionCooldownScope.AllSkills, ReflectionCooldownMode.Resolve(new AppSettingsData { ReflectionCooldownMode = "all-skills" }));
        var warnings = new List<DiagnosticEvent>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == SkillCatalog.Category && e.Level == DiagnosticLevel.Warning) warnings.Add(e); };
        DiagnosticLog.Emitted += capture;
        try
        {
            Assert.Equal(ReflectionCooldownScope.LastWrittenSkill, ReflectionCooldownMode.Resolve(new AppSettingsData { ReflectionCooldownMode = "everything" }));
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        var warning = Assert.Single(warnings);
        Assert.Equal("ReflectionCooldownMode='everything' is not one of all-skills, last-written-skill. Using last-written-skill.", warning.Message);
    }
}
