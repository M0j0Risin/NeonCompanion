using NeonCompanion.Files;

namespace NeonCompanion.Tests;

public sealed class PathGlobTests
{
    [Theory]
    [InlineData("*.cs", false)]
    [InlineData("report*", false)]
    [InlineData("notes.txt", false)]
    [InlineData("src/*.cs", true)]
    [InlineData(@"src\*.cs", true)]
    [InlineData("**/*.cs", true)]
    [InlineData("**", true)]
    public void IsPathPattern_SeesASeparatorOrADoubleStar(string pattern, bool expected) =>
        Assert.Equal(expected, PathGlob.IsPathPattern(pattern));

    [Theory]
    [InlineData("src/**/*.cs", "src/a.cs", true)]
    [InlineData("src/**/*.cs", "src/deep/er/a.cs", true)]
    [InlineData("src/**/*.cs", @"src\deep\a.cs", true)]
    [InlineData("src/**/*.cs", "SRC/A.CS", true)]
    [InlineData("src/**/*.cs", "a.cs", false)]
    [InlineData("src/**/*.cs", "src/a.txt", false)]
    [InlineData("src/**/*.cs", "lib/src/a.cs", false)]
    [InlineData("**/src/*.cs", "lib/src/a.cs", true)]
    [InlineData("**/src/*.cs", "src/a.cs", true)]
    [InlineData("**/src/*.cs", "src/deep/a.cs", false)]
    [InlineData("src/*/*.cs", "src/deep/a.cs", true)]
    [InlineData("src/*/*.cs", "src/a.cs", false)]
    [InlineData("**", "anything/at/all", true)]
    [InlineData("**/*", "a", true)]
    [InlineData("docs/?.md", "docs/a.md", true)]
    [InlineData("docs/?.md", "docs/ab.md", false)]
    [InlineData("/src/*.cs", "src/a.cs", true)]
    [InlineData("src/**/**/*.cs", "src/a.cs", true)]
    [InlineData("src/", "src", true)]
    public void IsMatch_WalksSegments(string pattern, string path, bool expected) =>
        Assert.Equal(expected, PathGlob.IsMatch(pattern, path));

    [Theory]
    [InlineData("*.cs", "a.cs", true)]
    [InlineData("*.cs", "a.csx", false)]
    [InlineData("a*b*c", "aXXbYYc", true)]
    [InlineData("a*b*c", "abc", true)]
    [InlineData("a*b*c", "ac", false)]
    [InlineData("?.md", "a.md", true)]
    [InlineData("?.md", ".md", false)]
    [InlineData("*", "", true)]
    [InlineData("", "", true)]
    [InlineData("", "x", false)]
    [InlineData("README", "readme", true)]
    public void MatchSegment_StarAndQuestionMark_IgnoringCase(string pattern, string text, bool expected) =>
        Assert.Equal(expected, PathGlob.MatchSegment(pattern, text));

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => PathGlob.IsMatch(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => PathGlob.IsMatch("a", null!));
        Assert.Throws<ArgumentNullException>(() => PathGlob.MatchSegment(null!, "a"));
    }
}
