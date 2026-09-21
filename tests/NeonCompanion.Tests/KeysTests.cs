using NeonCompanion.UI;

namespace NeonCompanion.Tests;

public class KeysTests
{
    [Fact]
    public void IsCancel_IsEscape()
    {
        Assert.True(Keys.IsCancel(Keys.Escape));
        Assert.False(Keys.IsCancel(Keys.Ctrl(ConsoleKey.Q)));
        Assert.False(Keys.IsCancel(Keys.Char('e')));
    }

    [Fact]
    public void IsInterrupt_IsCtrlC_WithoutAlt()
    {
        // The console's own shape (ETX with the key) and the test factory's ('\0') both count;
        // Shift is ignored, Alt (AltGr+C) is not it, and no other key is.
        Assert.True(Keys.IsInterrupt(Keys.CtrlC));
        Assert.True(Keys.IsInterrupt(Keys.Ctrl(ConsoleKey.C)));
        Assert.True(Keys.IsInterrupt(new ConsoleKeyInfo('\x03', ConsoleKey.C, shift: true, alt: false, control: true)));
        Assert.False(Keys.IsInterrupt(new ConsoleKeyInfo('\0', ConsoleKey.C, shift: false, alt: true, control: true)));
        Assert.False(Keys.IsInterrupt(Keys.Escape));
        Assert.False(Keys.IsInterrupt(Keys.Ctrl(ConsoleKey.Q)));
        Assert.False(Keys.IsInterrupt(Keys.Char('c')));
        // Spectre's TestConsoleInput.PushText marks an upper-case letter with Control: a typed "C" is a character, never the chord.
        Assert.False(Keys.IsInterrupt(new ConsoleKeyInfo('C', ConsoleKey.C, shift: false, alt: false, control: true)));
        Assert.False(Keys.IsCancel(Keys.CtrlC));
    }

    [Fact]
    public void CtrlC_IsTheConsolesShape()
    {
        Assert.Equal('\x03', Keys.CtrlC.KeyChar);
        Assert.Equal(ConsoleKey.C, Keys.CtrlC.Key);
        Assert.Equal(ConsoleModifiers.Control, Keys.CtrlC.Modifiers);
    }

    [Fact]
    public void F4_IsABareKey()
    {
        Assert.Equal(ConsoleKey.F4, Keys.F4.Key);
        Assert.Equal('\0', Keys.F4.KeyChar);
        Assert.False(Keys.IsCancel(Keys.F4));
    }

    [Fact]
    public void Char_CarriesTheCharacterWithNoKey()
    {
        var key = Keys.Char('é');
        Assert.Equal('é', key.KeyChar);
        Assert.Equal(ConsoleKey.None, key.Key);
        Assert.Equal((ConsoleModifiers)0, key.Modifiers);
    }
}

public class PromptResultTests
{
    [Fact]
    public void Canceled_IsOneSharedInstance()
    {
        Assert.Same(PromptResult<string>.Canceled, PromptResult<string>.Canceled);
        Assert.True(PromptResult<string>.Canceled.IsCanceled);
        Assert.Null(PromptResult<string>.Canceled.Value);
    }

    [Fact]
    public void From_WrapsTheValue_WithRecordEquality()
    {
        Assert.Equal(PromptResult<string>.From("a"), PromptResult<string>.From("a"));
        Assert.NotEqual(PromptResult<string>.From("a"), PromptResult<string>.Canceled);
        Assert.Throws<ArgumentNullException>(() => PromptResult<string>.From(null!));
    }
}

public class TextCellsTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 3)]
    [InlineData("日本語", 6)]
    [InlineData("a日b", 4)]
    [InlineData("😀", 2)]
    [InlineData("x😀y", 4)]
    [InlineData("⏰", 2)]
    [InlineData("⏳", 2)]
    [InlineData("✋", 2)]                    // ✋ (2026-09-18): the interrupt glyph, a BMP character Unicode lists as Wide
    [InlineData("✊", 2)]
    [InlineData("🎤 👂 ✋", 8)]              // the speech strip with the interrupt
    [InlineData("⏰ tea 09:00", 12)]
    [InlineData("\U0001F5D1", 2)]                // 🗑 bare: a surrogate pair
    [InlineData("\U0001F5D1️", 2)]          // 🗑️ (2026-09-18): the variation selector is zero, drawn inside the pair's two cells
    [InlineData("\U0001F5D1️ ", 3)]
    [InlineData("⚙️", 2)]              // ⚙️ (2026-09-19): the gear is Neutral, one cell bare; U+FE0F makes the two-cell emoji, so the selector counts one
    [InlineData("✂️", 2)]              // ✂️: the prune lines' scissors, the same shape
    [InlineData("✂️ x", 4)]
    [InlineData("✋️", 2)]              // a selector after a wide BMP character stays zero
    [InlineData("\U0001F5DC️", 2)]          // 🗜️: the compact lines' clamp, a pair + the selector
    [InlineData("\U0001F6D1", 2)]                // 🛑: the stop line's sign, a pair alone
    [InlineData("️", 0)]                    // a selector with nothing before it
    [InlineData("️️", 0)]              // two selectors: the second follows a selector, not a character
    public void Width_CountsCells(string text, int cells)
    {
        Assert.Equal(cells, TextCells.Width(text));
    }

    [Fact]
    public void Width_AVariationSelectorIsZero()
    {
        Assert.Equal(0, TextCells.Width('️'));
        Assert.Equal(0, TextCells.Width('︎'));
    }

    [Fact]
    public void ElementLengthBefore_StepsOverASurrogatePair()
    {
        Assert.Equal(2, TextCells.ElementLengthBefore("a😀", 3));
        Assert.Equal(1, TextCells.ElementLengthBefore("ab", 2));
        Assert.Equal(0, TextCells.ElementLengthBefore("ab", 0));
    }
}
