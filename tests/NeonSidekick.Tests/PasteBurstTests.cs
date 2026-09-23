using NeonSidekick.UI;

namespace NeonSidekick.Tests;

public class PasteBurstTests
{
    private static (ConsoleKeyInfo, int) K(ConsoleKeyInfo key, int repeat = 1) => (key, repeat);

    /// <summary>The live Enter record: VK_RETURN with a carriage return.</summary>
    private static readonly ConsoleKeyInfo LiveEnter = new('\r', ConsoleKey.Enter, false, false, false);

    private static IReadOnlyList<InputEvent> Coalesce(bool joins, out bool endsAsPaste, params (ConsoleKeyInfo, int)[] run) =>
        PasteBurst.Coalesce(run, joins, out endsAsPaste);

    [Fact]
    public void OneKey_IsAKey()
    {
        var events = Coalesce(false, out bool paste, K(Keys.Char('a')));
        Assert.Equal(new InputEvent[] { new InputEvent.Key(Keys.Char('a')) }, events);
        Assert.False(paste);

        events = Coalesce(false, out paste, K(LiveEnter));
        Assert.Equal(new InputEvent[] { new InputEvent.Key(LiveEnter) }, events);
        Assert.False(paste);

        Assert.Empty(Coalesce(false, out paste));
        Assert.False(paste);
    }

    [Fact]
    public void TwoOrMoreTextKeysTogether_AreOnePaste()
    {
        var events = Coalesce(false, out bool paste, K(Keys.Char('a')), K(Keys.Char('b')));
        Assert.Equal(new InputEvent[] { new InputEvent.Paste("ab") }, events);
        Assert.True(paste);

        // Live line breaks and a test's '\0' Enter both read as '\n'; a tab is a tab.
        events = Coalesce(false, out paste, K(Keys.Char('a')), K(LiveEnter), K(Keys.Tab), K(Keys.Char('b')), K(Keys.Enter));
        Assert.Equal(new InputEvent[] { new InputEvent.Paste("a\n\tb\n") }, events);
        Assert.True(paste);
    }

    [Fact]
    public void ABareKey_SplitsTheRun_AndIsItself()
    {
        var events = Coalesce(false, out bool paste, K(Keys.Char('a')), K(Keys.Char('b')), K(Keys.Left), K(Keys.Char('c')), K(Keys.Char('d')), K(Keys.F4));
        Assert.Equal(new InputEvent[] { new InputEvent.Paste("ab"), new InputEvent.Key(Keys.Left), new InputEvent.Paste("cd"), new InputEvent.Key(Keys.F4) }, events);
        Assert.False(paste);

        // ESC and a Ctrl chord are bare keys; a run of one either side stays a key.
        var ctrlA = new ConsoleKeyInfo('\x01', ConsoleKey.A, false, false, true);
        events = Coalesce(false, out paste, K(Keys.Char('a')), K(Keys.Escape), K(ctrlA), K(Keys.Char('b')));
        Assert.Equal(new InputEvent[] { new InputEvent.Key(Keys.Char('a')), new InputEvent.Key(Keys.Escape), new InputEvent.Key(ctrlA), new InputEvent.Key(Keys.Char('b')) }, events);
    }

    [Fact]
    public void AHeldKey_IsNeverPasteMaterial()
    {
        // A repeat count is a key held down: that many keys, and it ends any run around it.
        var events = Coalesce(false, out bool paste, K(Keys.Char('a')), K(Keys.Char('b'), 3), K(Keys.Char('c')));
        Assert.Equal(new InputEvent[] { new InputEvent.Key(Keys.Char('a')), new InputEvent.Key(Keys.Char('b')), new InputEvent.Key(Keys.Char('b')), new InputEvent.Key(Keys.Char('b')), new InputEvent.Key(Keys.Char('c')) }, events);
        Assert.False(paste);

        events = Coalesce(false, out paste, K(LiveEnter, 2));
        Assert.Equal(new InputEvent[] { new InputEvent.Key(LiveEnter), new InputEvent.Key(LiveEnter) }, events);
    }

    [Fact]
    public void WithinTheGraceOfAPaste_ALoneLineBreak_IsPartOfIt()
    {
        // ConPTY's next chunk happened to be one '\r': a paste, not Enter.
        var events = Coalesce(true, out bool paste, K(LiveEnter));
        Assert.Equal(new InputEvent[] { new InputEvent.Paste("\n") }, events);
        Assert.True(paste);

        // Only the first run joins; after a bare key the usual rule is back.
        events = Coalesce(true, out paste, K(Keys.Char('x')), K(Keys.Left), K(Keys.Char('y')));
        Assert.Equal(new InputEvent[] { new InputEvent.Paste("x"), new InputEvent.Key(Keys.Left), new InputEvent.Key(Keys.Char('y')) }, events);
        Assert.False(paste);
    }

    [Fact]
    public void TextOf_IsPinned()
    {
        Assert.Equal('a', PasteBurst.TextOf(Keys.Char('a')));
        Assert.Equal('日', PasteBurst.TextOf(Keys.Char('日')));
        Assert.Equal('\n', PasteBurst.TextOf(LiveEnter));
        Assert.Equal('\n', PasteBurst.TextOf(Keys.Enter));
        Assert.Equal('\t', PasteBurst.TextOf(Keys.Tab));
        // An AltGr letter carries Ctrl+Alt and is still its character.
        Assert.Equal('@', PasteBurst.TextOf(new ConsoleKeyInfo('@', ConsoleKey.Q, false, true, true)));
        Assert.Null(PasteBurst.TextOf(Keys.Left));
        Assert.Null(PasteBurst.TextOf(Keys.Escape));
        Assert.Null(PasteBurst.TextOf(Keys.Backspace));
        Assert.Null(PasteBurst.TextOf(new ConsoleKeyInfo('\x01', ConsoleKey.A, false, false, true)));
    }

    [Fact]
    public void Coalesce_RejectsNull() => Assert.Throws<ArgumentNullException>(() => PasteBurst.Coalesce(null!, false, out _));
}
