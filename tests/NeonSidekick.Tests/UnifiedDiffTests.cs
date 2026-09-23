using NeonSidekick.Git;

namespace NeonSidekick.Tests;

/// <summary><see cref="UnifiedDiff"/> against Python's <c>difflib.unified_diff</c> (the expected patches were generated with it, 2026-09-20) and git's no-newline marker.</summary>
public sealed class UnifiedDiffTests
{
    private static string Patch(string a, string b, int context = 3)
    {
        var hunks = UnifiedDiff.Hunks(UnifiedDiff.Split(a), UnifiedDiff.Split(b), context);
        Assert.NotNull(hunks);
        return UnifiedDiff.Format("a/x", "b/x", hunks!);
    }

    private static string Lines(params string[] lines) => string.Concat(lines.Select(l => l + "\n"));

    private static string Numbers(Func<int, string?>? change = null) => Lines(Enumerable.Range(1, 20).Select(i => change?.Invoke(i) ?? i.ToString()).ToArray());

    [Fact]
    public void Split_KeepsTheLines_AndWhetherTheTextEndedWithANewline()
    {
        Assert.Equal(UnifiedDiff.Text.Empty, UnifiedDiff.Split(""));
        var lf = UnifiedDiff.Split("one\ntwo\n");
        Assert.Equal(["one", "two"], lf.Lines);
        Assert.True(lf.EndsWithNewline);
        var bare = UnifiedDiff.Split("one\ntwo");
        Assert.Equal(["one", "two"], bare.Lines);
        Assert.False(bare.EndsWithNewline);
        var crlf = UnifiedDiff.Split("one\r\ntwo\r\n");
        Assert.Equal(["one", "two"], crlf.Lines);
        Assert.True(crlf.EndsWithNewline);
        Assert.Equal([""], UnifiedDiff.Split("\n").Lines);
        Assert.Equal(["", ""], UnifiedDiff.Split("\n\n").Lines);
    }

    [Fact]
    public void Identical_Texts_HaveNoHunks_AndAnEmptyPatch()
    {
        var hunks = UnifiedDiff.Hunks(UnifiedDiff.Split("one\ntwo\n"), UnifiedDiff.Split("one\r\ntwo\r\n"));
        Assert.Equal([], hunks);
        Assert.Equal("", UnifiedDiff.Format("a/x", "b/x", hunks!));
        Assert.Equal((0, 0), UnifiedDiff.Count(hunks!));
        Assert.Equal([], UnifiedDiff.Hunks(UnifiedDiff.Text.Empty, UnifiedDiff.Text.Empty));
    }

    [Fact]
    public void Insert_Delete_AndReplace_MatchDifflib()
    {
        Assert.Equal("--- a/x\n+++ b/x\n@@ -1,3 +1,4 @@\n one\n two\n+2.5\n three\n", Patch(Lines("one", "two", "three"), Lines("one", "two", "2.5", "three")));
        Assert.Equal("--- a/x\n+++ b/x\n@@ -1,3 +1,2 @@\n one\n-two\n three\n", Patch(Lines("one", "two", "three"), Lines("one", "three")));
        Assert.Equal("--- a/x\n+++ b/x\n@@ -1,3 +1,3 @@\n one\n-two\n+2\n three\n", Patch(Lines("one", "two", "three"), Lines("one", "2", "three")));
    }

    [Fact]
    public void ChangesFarApart_MakeTwoHunks_NearOnes_Merge()
    {
        string far = Patch(Numbers(), Numbers(i => i is 2 or 19 ? "x" + i : null));
        Assert.Equal("--- a/x\n+++ b/x\n@@ -1,5 +1,5 @@\n 1\n-2\n+x2\n 3\n 4\n 5\n@@ -16,5 +16,5 @@\n 16\n 17\n 18\n-19\n+x19\n 20\n", far);
        string near = Patch(Numbers(), Numbers(i => i is 5 or 10 ? "x" + i : null));
        Assert.Equal("--- a/x\n+++ b/x\n@@ -2,12 +2,12 @@\n 2\n 3\n 4\n-5\n+x5\n 6\n 7\n 8\n 9\n-10\n+x10\n 11\n 12\n 13\n", near);
        var hunks = UnifiedDiff.Hunks(UnifiedDiff.Split(Numbers()), UnifiedDiff.Split(Numbers(i => i is 2 or 19 ? "x" + i : null)));
        Assert.Equal(2, hunks!.Count);
        Assert.Equal((1, 5, 1, 5), (hunks[0].OldStart, hunks[0].OldCount, hunks[0].NewStart, hunks[0].NewCount));
        Assert.Equal((2, 2), UnifiedDiff.Count(hunks));
    }

    [Fact]
    public void AWholeFileAdded_OrRemoved_MatchesDifflib()
    {
        Assert.Equal("--- a/x\n+++ b/x\n@@ -0,0 +1,2 @@\n+a\n+b\n", Patch("", Lines("a", "b")));
        Assert.Equal("--- a/x\n+++ b/x\n@@ -1,2 +0,0 @@\n-a\n-b\n", Patch(Lines("a", "b"), ""));
        Assert.Equal("--- a/x\n+++ b/x\n@@ -0,0 +1 @@\n+a\n", Patch("", "a\n"));
    }

    [Fact]
    public void ZeroContext_ShowsTheChangedLinesAlone()
    {
        Assert.Equal("--- a/x\n+++ b/x\n@@ -2 +2 @@\n-two\n+2\n", Patch(Lines("one", "two", "three"), Lines("one", "2", "three"), context: 0));
    }

    [Fact]
    public void AMissingTrailingNewline_IsADifferentLastLine_WithGitsMarker()
    {
        // The same text with and without the newline differs, as in git.
        Assert.Equal("--- a/x\n+++ b/x\n@@ -1,2 +1,2 @@\n one\n-two\n+two\n\\ No newline at end of file\n", Patch("one\ntwo\n", "one\ntwo"));
        Assert.Equal("--- a/x\n+++ b/x\n@@ -1,2 +1,2 @@\n one\n-two\n\\ No newline at end of file\n+two\n", Patch("one\ntwo", "one\ntwo\n"));
        // Both without: the shared last line carries one marker as context.
        Assert.Equal("--- a/x\n+++ b/x\n@@ -1,2 +1,2 @@\n-one\n+1\n two\n\\ No newline at end of file\n", Patch("one\ntwo", "1\ntwo"));
        var hunks = UnifiedDiff.Hunks(UnifiedDiff.Split("one\ntwo\n"), UnifiedDiff.Split("one\ntwo"));
        Assert.Equal((1, 1), UnifiedDiff.Count(hunks!));   // the marker is not a line
    }

    [Fact]
    public void TooManyDistinctLines_IsNull()
    {
        var a = new UnifiedDiff.Text(Enumerable.Range(0, UnifiedDiff.MaxDistinctLines).Select(i => "a" + i).ToList(), true);
        var b = new UnifiedDiff.Text(["b"], true);
        Assert.Null(UnifiedDiff.Hunks(a, b));
        var fits = new UnifiedDiff.Text(Enumerable.Range(0, UnifiedDiff.MaxDistinctLines - 1).Select(i => "a" + i).ToList(), true);
        Assert.NotNull(UnifiedDiff.Hunks(fits, b));
    }

    [Fact]
    public void ABigText_WithPopularLines_StillDiffs()
    {
        // 300 lines, most of them blank: difflib's autojunk drops the popular line from the index; the change is still found.
        string before = Lines(Enumerable.Range(0, 300).Select(i => i % 3 == 0 ? "line " + i : "").ToArray());
        string after = before.Replace("line 150\n", "changed 150\n", StringComparison.Ordinal);
        var hunks = UnifiedDiff.Hunks(UnifiedDiff.Split(before), UnifiedDiff.Split(after));
        Assert.Equal((1, 1), UnifiedDiff.Count(hunks!));
        Assert.Contains("-line 150", hunks![0].Lines);
        Assert.Contains("+changed 150", hunks[0].Lines);
    }
}
