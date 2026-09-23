using NeonSidekick.UI.Markdown;

namespace NeonSidekick.Tests;

public class CodeLanguagesTests
{
    [Theory]
    [InlineData("csharp", "csharp")]
    [InlineData("C#", "csharp")]
    [InlineData("cs", "csharp")]
    [InlineData("PY", "python")]
    [InlineData("sh", "bash")]
    [InlineData("pwsh", "powershell")]
    [InlineData("yml", "yaml")]
    [InlineData("ini", "toml")]
    [InlineData("tsx", "typescript")]
    [InlineData("cpp", "c")]
    [InlineData("rs", "rust")]
    [InlineData("html", "xml")]
    [InlineData("patch", "diff")]
    public void Find_ResolvesAliases_CaseInsensitively(string fence, string name)
    {
        Assert.Equal(name, CodeLanguages.Find(fence)?.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("text")]
    [InlineData("brainfuck")]
    public void Find_UnknownOrMissing_IsNull(string? fence)
    {
        Assert.Null(CodeLanguages.Find(fence));
    }
}
