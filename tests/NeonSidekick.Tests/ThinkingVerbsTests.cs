using NeonSidekick.App;

namespace NeonSidekick.Tests;

public sealed class ThinkingVerbsTests
{
    [Fact]
    public void All_IsTheUsersList_LowercaseAndDistinct()
    {
        Assert.Equal(122, ThinkingVerbs.All.Length);
        Assert.Equal("bellowing", ThinkingVerbs.All[0]);
        Assert.Equal("zipping", ThinkingVerbs.All[^1]);
        Assert.Contains("snorkeling", ThinkingVerbs.All);
        Assert.DoesNotContain("bafflering", ThinkingVerbs.All);   // the first list is gone whole
        Assert.All(ThinkingVerbs.All, v => Assert.Equal(v.ToLowerInvariant(), v));
        Assert.All(ThinkingVerbs.All, v => Assert.Matches("^[a-z]+$", v));
        Assert.Equal(ThinkingVerbs.All.Length, ThinkingVerbs.All.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(ChatScreen.ThinkingLabel, ThinkingVerbs.All);
    }

    [Fact]
    public void Pick_DrawsFromTheList_AndTheSameSeedDrawsTheSameVerb()
    {
        var random = new Random(11);
        for (int i = 0; i < 200; i++)
        {
            Assert.Contains(ThinkingVerbs.Pick(random), ThinkingVerbs.All);
        }

        Assert.Equal(ThinkingVerbs.Pick(new Random(7)), ThinkingVerbs.Pick(new Random(7)));
        Assert.Throws<ArgumentNullException>(() => ThinkingVerbs.Pick(null!));
    }

    [Fact]
    public void Pick_WithAnExclusion_NeverDrawsIt_AndNullExcludesNothing()
    {
        var random = new Random(3);
        for (int i = 0; i < 500; i++)
        {
            Assert.NotEqual("zipping", ThinkingVerbs.Pick(random, "zipping"));
        }

        // The same seed, the same draw when nothing is excluded — and the first draw again when
        // the exclusion is another word.
        Assert.Equal(ThinkingVerbs.Pick(new Random(7)), ThinkingVerbs.Pick(new Random(7), null));
        string first = ThinkingVerbs.Pick(new Random(7));
        Assert.Equal(first, ThinkingVerbs.Pick(new Random(7), first == "zipping" ? "bellowing" : "zipping"));
        Assert.Throws<ArgumentNullException>(() => ThinkingVerbs.Pick(null!, "zipping"));
    }
}
