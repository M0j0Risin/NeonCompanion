using NeonCompanion.Files;
using static NeonCompanion.Files.SequenceSimilarity;

namespace NeonCompanion.Tests;

/// <summary>The difflib port: every figure below was produced by CPython's <c>difflib.SequenceMatcher(None, a, b)</c> and is pinned as such.</summary>
public class SequenceSimilarityTests
{
    [Fact]
    public void Ratio_IsDifflibs()
    {
        Assert.Equal(0.75, Ratio("abcd", "bcde"));
        Assert.Equal(0.6153846153846154, Ratio("kitten", "sitting"), 12);
        Assert.Equal(1.0, Ratio("", ""));
        Assert.Equal(0.0, Ratio("abc", ""));
        Assert.Equal(0.0, Ratio("", "abc"));
        Assert.Equal(1.0, Ratio("same", "same"));
        Assert.Equal(0.0, Ratio("abc", "xyz"));
        Assert.Equal(Ratio("private Thread currentThread;", "private volatile Thread currentThread;"), Ratio("private volatile Thread currentThread;", "private Thread currentThread;"), 12);
        Assert.Equal(0.8656716417910447, Ratio("private Thread currentThread;", "private volatile Thread currentThread;"), 12);
    }

    [Fact]
    public void MatchingBlocks_AreSorted_Merged_AndEndWithTheSentinel()
    {
        Assert.Equal(
            [new Block(0, 0, 2), new Block(3, 2, 2), new Block(5, 4, 0)],
            MatchingBlocks("abxcd", "abcd"));
        Assert.Equal([new Block(4, 4, 0)], MatchingBlocks("abcd", "wxyz"));
        // Adjacent blocks found by two halves of the divide collapse into one, as difflib's do.
        Assert.Equal([new Block(0, 0, 6), new Block(6, 6, 0)], MatchingBlocks("abcdef", "abcdef"));
    }

    [Fact]
    public void Opcodes_AreDifflibs()
    {
        Assert.Equal(
            [
                new Opcode(Op.Delete, 0, 1, 0, 0),
                new Opcode(Op.Equal, 1, 3, 0, 2),
                new Opcode(Op.Replace, 3, 4, 2, 3),
                new Opcode(Op.Equal, 4, 6, 3, 5),
                new Opcode(Op.Insert, 6, 6, 5, 6),
            ],
            Opcodes("qabxcd", "abycdf"));
        Assert.Equal([new Opcode(Op.Equal, 0, 3, 0, 3)], Opcodes("abc", "abc"));
        Assert.Equal([new Opcode(Op.Insert, 0, 0, 0, 3)], Opcodes("", "abc"));
    }

    [Fact]
    public void AutoJunk_DropsThePopularCharactersOfALongSecondSequence()
    {
        // 300 'a's and a 'b' at opposite ends: 'a' is popular (over 1 % of a 200+ sequence) and out of the index, so the only
        // block found through it is the lone 'b' — difflib's 0.0033, not the 0.9967 a full index would give. The port agrees.
        Assert.Equal(0.0033222591362126247, Ratio(new string('a', 300) + "b", "b" + new string('a', 300)), 12);
        Assert.Equal(0.0, Ratio(string.Concat(Enumerable.Repeat("ab", 150)), string.Concat(Enumerable.Repeat("ba", 150))));
        // An identical pair still scores 1.0: the empty match extends over the popular characters from the start.
        Assert.Equal(1.0, Ratio(new string('a', 300), new string('a', 300)));
        Assert.Equal(1.0, Ratio(new string('a', 199), new string('a', 199)));
        Assert.Equal(200, AutoJunkMinLength);
    }
}
