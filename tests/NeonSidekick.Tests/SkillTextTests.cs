using NeonSidekick.Sessions;
using NeonSidekick.Skills;
using NeonSidekick.Tests.Fakes;

namespace NeonSidekick.Tests;

public class SkillTextTests
{
    private static readonly TimeZoneInfo Zone = ManualTimeProvider.DefaultZone;
    private static readonly DateTimeOffset At = ManualTimeProvider.DefaultUtcNow;   // 2026-09-11 14:05 in the zone

    [Fact]
    public void CleanSummary_CollapsesWhitespace_CutsAtTheCap_EmptyForBlank()
    {
        // The reflection's summary line (2026-09-19).
        Assert.Equal(300, SkillText.MaxSummaryChars);
        Assert.Equal("", SkillText.CleanSummary(null));
        Assert.Equal("", SkillText.CleanSummary(" \n\t "));
        Assert.Equal("Added the retry. Kept the rest.", SkillText.CleanSummary("  Added   the\r\nretry.\n\n  Kept the rest.\n"));
        string cut = SkillText.CleanSummary(new string('x', 299) + " " + new string('y', 20));
        Assert.Equal(300, cut.Length);
        Assert.Equal(new string('x', 299) + "…", cut);
        Assert.Equal(new string('x', 300), SkillText.CleanSummary(new string('x', 300)));   // exactly the cap: whole
    }

    [Fact]
    public void UsageLine_IsTheFactsThatExist_JoinedBySemicolons()
    {
        // The reflection's catalog and the Skills › name page (2026-09-19).
        Assert.Equal("loaded in 12 turns across 6 sessions, 4 with errors; last loaded 2026-09-11 14:05; written by a reflection 2× (updated 2026-09-11 14:05)",
            SkillText.UsageLine(new SkillUsage(12, 6, 4, At), new ReflectionMark(At, "x", ReflectionRow.Updated), 2, Zone));
        Assert.Equal("loaded in 1 turn across 1 session; last loaded 2026-09-11 14:05", SkillText.UsageLine(new SkillUsage(1, 1, 0, At), null, 0, Zone));
        Assert.Equal("written by a reflection 1× (created 2026-09-11 14:05)", SkillText.UsageLine(null, new ReflectionMark(At, "x", ReflectionRow.Created), 1, Zone));
        Assert.Equal("never loaded in a stored session", SkillText.UsageLine(null, null, 0, Zone));
        Assert.Equal(SkillText.NeverLoaded, SkillText.UsageLine(null, new ReflectionMark(At, "x", ReflectionRow.Created), 0, Zone));   // a mark with no count is no fact
        Assert.Throws<ArgumentNullException>(() => SkillText.UsageLine(null, null, 0, null!));
    }
}
