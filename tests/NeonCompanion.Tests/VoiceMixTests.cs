using NeonCompanion.Speech;

namespace NeonCompanion.Tests;

public class VoiceMixTests
{
    [Fact]
    public void Spec_NoSecondaryVoice_IsThePrimaryAlone()
    {
        Assert.Equal("af_heart", VoiceMix.Spec("af_heart", "", 50));
        Assert.Equal("af_heart", VoiceMix.Spec("af_heart", null, 50));
        Assert.Equal("af_heart", VoiceMix.Spec(" af_heart ", "   ", 50));
    }

    [Fact]
    public void Spec_TwoVoices_IsKokorosWeightedForm()
    {
        Assert.Equal("af_heart(70)+af_sky(30)", VoiceMix.Spec("af_heart", "af_sky", 70));
        Assert.Equal("af_heart(50)+af_sky(50)", VoiceMix.Spec("af_heart", "af_sky", 50));
        Assert.Equal("af_heart(1)+af_sky(99)", VoiceMix.Spec(" af_heart", "af_sky ", 1));
    }

    [Fact]
    public void TryParse_IsTheInverseOfSpec()
    {
        Assert.True(VoiceMix.TryParse("af_heart", out var one));
        Assert.Equal(new[] { ("af_heart", 100) }, one);

        Assert.True(VoiceMix.TryParse("af_heart(70)+af_sky(30)", out var two));
        Assert.Equal(new[] { ("af_heart", 70), ("af_sky", 30) }, two);

        Assert.True(VoiceMix.TryParse(" af_heart (70) + af_sky(30) ", out var spaced));
        Assert.Equal(new[] { ("af_heart", 70), ("af_sky", 30) }, spaced);

        foreach (var (primary, secondary, mix) in new[] { ("af_heart", "af_sky", 70), ("af_heart", "", 50), ("af_heart", "af_sky", 0) })
        {
            Assert.True(VoiceMix.TryParse(VoiceMix.Spec(primary, secondary, mix), out var parts));
            Assert.Equal(VoiceMix.Voices(primary, secondary, mix), parts.Select(p => p.Name).ToArray());
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("af_heart(70")]
    [InlineData("af_heart(x)+af_sky(30)")]
    [InlineData("(70)+af_sky(30)")]
    [InlineData("af_heart+")]
    [InlineData("af_heart(-1)")]
    public void TryParse_RefusesAMalformedSpec(string? spec)
    {
        Assert.False(VoiceMix.TryParse(spec, out _));
    }

    [Fact]
    public void Spec_TheEndsOfTheRange_CollapseToOneVoice()
    {
        Assert.Equal("af_heart", VoiceMix.Spec("af_heart", "af_sky", 100));
        Assert.Equal("af_heart", VoiceMix.Spec("af_heart", "af_sky", 150));
        Assert.Equal("af_sky", VoiceMix.Spec("af_heart", "af_sky", 0));
        Assert.Equal("af_sky", VoiceMix.Spec("af_heart", "af_sky", -5));
    }

    [Fact]
    public void Spec_TheSameVoiceTwice_IsThatVoiceOnce()
    {
        Assert.Equal("af_heart", VoiceMix.Spec("af_heart", "af_heart", 30));
        Assert.Equal("af_heart(30)+AF_HEART(70)", VoiceMix.Spec("af_heart", "AF_HEART", 30));   // ordinal: the server decides case
    }

    [Fact]
    public void Voices_NamesWhatSpecSends()
    {
        Assert.Equal(new[] { "af_heart" }, VoiceMix.Voices("af_heart", "", 50));
        Assert.Equal(new[] { "af_heart", "af_sky" }, VoiceMix.Voices("af_heart", "af_sky", 50));
        Assert.Equal(new[] { "af_heart" }, VoiceMix.Voices("af_heart", "af_sky", 100));
        Assert.Equal(new[] { "af_sky" }, VoiceMix.Voices("af_heart", "af_sky", 0));
        Assert.Equal(new[] { "af_heart" }, VoiceMix.Voices("af_heart", "af_heart", 50));
    }
}
