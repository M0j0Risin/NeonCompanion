using System.Text.Json;
using NeonSidekick.Obsidian;

namespace NeonSidekick.Tests;

/// <summary>Obsidian's properties, the YAML frontmatter read and edited by hand (2026-09-22): the subset, and that an edit touches only the key it names.</summary>
public sealed class NotePropertiesTests
{
    private static readonly string[] Note =
    [
        "---",
        "title: \"A: plan\"",
        "# a comment Obsidian never writes",
        "tags:",
        "  - project",
        "  - project/alpha",
        "aliases: [Roadmap, 'The Plan']",
        "due: 2026-10-01",
        "meta:",
        "  owner: me",
        "notes: |",
        "  line one",
        "  line two",
        "---",
        "# Body",
    ];

    private static JsonElement Json(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    [Fact]
    public void Parse_ReadsScalars_Lists_Blocks_AndKeepsAMapOpaque()
    {
        var front = NoteProperties.Parse(Note);
        Assert.True(front.Present);
        Assert.Equal(13, front.Close);
        Assert.Equal(["title", "tags", "aliases", "due", "meta", "notes"], front.Properties.Select(p => p.Key));
        Assert.Equal("A: plan", front.Find("title")!.Value);
        Assert.Equal(["project", "project/alpha"], front.Find("tags")!.Items!);
        Assert.Equal(["Roadmap", "The Plan"], front.Find("ALIASES")!.Items!);
        Assert.Equal("2026-10-01", front.Find("due")!.Value);
        Assert.True(front.Find("meta")!.Opaque);
        Assert.Equal("line one\nline two", front.Find("notes")!.Value);
        Assert.Equal(["project", "project/alpha"], NoteProperties.Tags(front));
        Assert.Equal(["Roadmap", "The Plan"], NoteProperties.Aliases(front));
        Assert.Equal(14, NoteProperties.BodyStart(Note));
    }

    [Fact]
    public void Parse_WithoutFrontmatter_OrAnUnclosedOne_IsNone()
    {
        Assert.False(NoteProperties.Parse(["# Title", "tags: x"]).Present);
        Assert.False(NoteProperties.Parse(["---", "tags: x"]).Present);
        Assert.Equal(0, NoteProperties.BodyStart(["---", "tags: x"]));
    }

    [Fact]
    public void ScalarTags_SplitOnCommasAndSpaces_AndLoseTheirHash()
    {
        var front = NoteProperties.Parse(["---", "tags: #a, b c", "alias: One, Two words", "---"]);
        Assert.Equal(["a", "b", "c"], NoteProperties.Tags(front));
        Assert.Equal(["One", "Two words"], NoteProperties.Aliases(front));
    }

    [Fact]
    public void Set_ReplacesOnlyItsOwnLines_AndLeavesEveryOtherByteAlone()
    {
        Assert.True(NoteProperties.TryRender("due", Json("\"2026-11-15\""), out var rendered));
        var edited = NoteProperties.Set(Note, "due", rendered);
        Assert.Equal(Note.Length, edited.Count);
        Assert.Equal("due: 2026-11-15", edited[7]);   // Obsidian's own date shape, unquoted
        for (int i = 0; i < Note.Length; i++)
        {
            if (i != 7)
            {
                Assert.Equal(Note[i], edited[i]);
            }
        }
    }

    [Fact]
    public void Set_AList_WritesObsidiansShape_AndANewKeyGoesBeforeTheFence()
    {
        Assert.True(NoteProperties.TryRender("tags", Json("[\"done\"]"), out var tags));
        Assert.True(NoteProperties.TryRender("status", Json("\"draft\""), out var status));
        var edited = NoteProperties.Set(NoteProperties.Set(Note, "tags", tags), "status", status);
        Assert.Equal(["tags:", "  - done"], edited.Skip(3).Take(2));
        Assert.Equal("status: draft", edited[^3]);
        Assert.Equal("---", edited[^2]);
        Assert.Equal(["done"], NoteProperties.Tags(NoteProperties.Parse(edited)));
    }

    [Fact]
    public void Set_OnANoteWithoutFrontmatter_AddsOne()
    {
        Assert.True(NoteProperties.TryRender("done", Json("true"), out var rendered));
        Assert.Equal(["---", "done: true", "---", "# Title"], NoteProperties.Set(["# Title"], "done", rendered));
    }

    [Fact]
    public void Remove_DropsTheKeysLines_AndSaysWhenItWasNotThere()
    {
        var edited = NoteProperties.Remove(Note, "tags", out bool removed);
        Assert.True(removed);
        Assert.Equal(Note.Length - 3, edited.Count);
        Assert.Null(NoteProperties.Parse(edited).Find("tags"));
        NoteProperties.Remove(Note, "nope", out removed);
        Assert.False(removed);
    }

    [Theory]
    [InlineData("\"yes\"", "k: \"yes\"")]
    [InlineData("3.5", "k: 3.5")]
    [InlineData("false", "k: false")]
    [InlineData("null", "k:")]
    [InlineData("[]", "k: []")]
    [InlineData("\"plain words\"", "k: plain words")]
    public void TryRender_QuotesWhatYamlWouldReadOtherwise(string json, string expected)
    {
        Assert.True(NoteProperties.TryRender("k", Json(json), out var lines));
        Assert.Equal(expected, lines[0]);
    }

    [Fact]
    public void TryRender_RefusesANestedObject()
    {
        Assert.False(NoteProperties.TryRender("k", Json("{\"a\": 1}"), out _));
        Assert.False(NoteProperties.TryRender("k", Json("[{\"a\": 1}]"), out _));
    }

    [Fact]
    public void Matches_ComparesAnyItem_AnyCase_AndNumbersByValue()
    {
        var front = NoteProperties.Parse(["---", "status: Draft", "tags: [a, b]", "n: 2.0", "---"]);
        Assert.True(NoteProperties.Matches(front.Find("status")!, "draft"));
        Assert.True(NoteProperties.Matches(front.Find("tags")!, "B"));
        Assert.True(NoteProperties.Matches(front.Find("n")!, "2"));
        Assert.False(NoteProperties.Matches(front.Find("status")!, "done"));
        Assert.Equal("tags: [a, b]", NoteProperties.Describe(front.Find("tags")!));
    }
}
