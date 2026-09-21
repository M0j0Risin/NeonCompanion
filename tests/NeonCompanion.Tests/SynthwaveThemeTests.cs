using NeonCompanion.UI;
using Spectre.Console;

namespace NeonCompanion.Tests;

public class SynthwaveThemeTests
{
    [Fact]
    public void ToHex_RendersUppercaseSixDigitHex()
    {
        Assert.Equal("#FF2E97", Theme.ToHex(Theme.Magenta));
        Assert.Equal("#0B0416", Theme.ToHex(Theme.Bg));
    }

    [Fact]
    public void Palette_IsPinned()
    {
        Assert.Equal("#33E0FF", Theme.ToHex(Theme.Cyan));
        Assert.Equal("#B15BFF", Theme.ToHex(Theme.Purple));
        Assert.Equal("#7B2FF7", Theme.ToHex(Theme.DeepPurple));
        Assert.Equal("#FFC832", Theme.ToHex(Theme.Amber));
        Assert.Equal("#FF8A3D", Theme.ToHex(Theme.Orange));
        Assert.Equal("#F45B9B", Theme.ToHex(Theme.SunsetRed));
        Assert.Equal("#EFE6FF", Theme.ToHex(Theme.Ink));
        Assert.Equal("#9A8BB8", Theme.ToHex(Theme.Dim));
        Assert.Equal("#443C56", Theme.ToHex(Theme.Dimmer));
        Assert.Equal(Theme.Dimmer, Theme.Placeholder.Foreground);
        Assert.Equal("#160A28", Theme.ToHex(Theme.PanelBg));
        Assert.Equal("#3DF27A", Theme.ToHex(Theme.Good));
        Assert.Equal("#FF4D6D", Theme.ToHex(Theme.Bad));
        Assert.Equal(Theme.Amber, Theme.Warn);
    }

    [Fact]
    public void GradientMarkup_StartsCyanAndEndsAmber()
    {
        string markup = Theme.GradientMarkup("ab");
        Assert.StartsWith("[#33E0FF]a[/]", markup);
        Assert.EndsWith("[#FFC832]b[/]", markup);
    }

    [Fact]
    public void GradientMarkup_EscapesMarkupCharacters()
    {
        string markup = Theme.GradientMarkup("[");
        Assert.Equal("[#33E0FF][[[/]", markup);
        Assert.NotNull(new Markup(markup)); // parses
    }

    [Fact]
    public void Rule_ProducesExactlyWidthGlyphs()
    {
        string markup = Theme.Rule(43);
        int glyphs = markup.Count(c => c == '─');
        Assert.Equal(43, glyphs);
        Assert.NotNull(new Markup(markup));
    }

    [Fact]
    public void Rule_ZeroWidthIsEmpty()
    {
        Assert.Equal(string.Empty, Theme.Rule(0));
    }

    [Fact]
    public void AccentMarkup_EscapesText()
    {
        string markup = Theme.AccentMarkup("a[b]");
        Assert.Equal("[#FF2E97 bold]a[[b]][/]", markup);
    }

    [Fact]
    public void SampleGradient_Endpoints()
    {
        Assert.Equal(Theme.Cyan, Theme.SampleGradient(0, Theme.GradientStops));
        Assert.Equal(Theme.Amber, Theme.SampleGradient(1, Theme.GradientStops));
        Assert.Equal(Theme.Magenta, Theme.SampleGradient(0.5, Theme.GradientStops));
    }

    [Fact]
    public void Sun_HasNineRowsAndParses()
    {
        string sun = Theme.Sun();
        Assert.Equal(9, sun.Split('\n').Length);
        Assert.NotNull(new Markup(sun));
    }
}
