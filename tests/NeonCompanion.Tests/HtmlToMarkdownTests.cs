using NeonCompanion.Web;

namespace NeonCompanion.Tests;

public class HtmlToMarkdownTests
{
    private static readonly Uri Base = new("https://example.com/docs/page.html");

    private static string Md(string html) => HtmlToMarkdown.Convert(html, Base).Markdown;

    [Fact]
    public void Title_IsTheDocumentTitle_Collapsed()
    {
        var page = HtmlToMarkdown.Convert("<html><head><title>  Fish &amp;\n  Chips </title></head><body><svg><title>icon</title></svg><p>x</p></body></html>", Base);

        Assert.Equal("Fish & Chips", page.Title);
        Assert.Equal("x", page.Markdown);
    }

    [Fact]
    public void NoTitle_IsEmpty()
    {
        Assert.Equal("", HtmlToMarkdown.Convert("<p>x</p>").Title);
    }

    [Fact]
    public void Headings_Paragraphs_AndWhitespace()
    {
        Assert.Equal("# One\n\nSome text here.\n\n## Two\n\nMore.", Md("<h1>One</h1><p>Some \n  text\there.</p><h2>Two</h2><div>More.</div>"));
    }

    [Fact]
    public void EmptyHeading_LeavesNoMarker()
    {
        Assert.Equal("a\n\nb", Md("<p>a</p><h2>  </h2><p>b</p>"));
    }

    [Fact]
    public void Links_AreAbsolute_AndSkippedWhenUseless()
    {
        Assert.Equal("See [the docs](https://example.com/docs/intro) and [there](https://other.org/x).", Md("<p>See <a href=\"intro\">the docs</a> and <a href=\"https://other.org/x\">there</a>.</p>"));
        Assert.Equal("top link none", Md("<a href=\"#top\">top</a> <a href=\"javascript:void(0)\">link</a> <a>none</a>"));
        Assert.Equal("https://example.com/", Md("<a href=\"https://example.com/\">https://example.com/</a>"));
        Assert.Equal("x", Md("<a href=\"/y\"></a>x"));
    }

    [Fact]
    public void BaseHref_IsHonoured()
    {
        Assert.Equal("[a](https://cdn.example.net/root/a.html)", Md("<head><base href=\"https://cdn.example.net/root/\"></head><a href=\"a.html\">a</a>"));
    }

    [Fact]
    public void LinkAroundBlocks_CollapsesToOneLine()
    {
        Assert.Equal("[Card title and text](https://example.com/card)", Md("<a href=\"/card\"><div><h3>Card title</h3><p>and text</p></div></a>"));
    }

    [Fact]
    public void Lists_NestAndNumber()
    {
        Assert.Equal("- one\n- two\n  1. a\n  2. b\n- three", Md("<ul><li>one</li><li>two<ol><li>a</li><li>b</li></ol></li><li>three</li></ul>"));
    }

    [Fact]
    public void EmptyItems_LeaveNoBullet()
    {
        Assert.Equal("- one\n- two", Md("<ul><li><img src=\"icon.svg\"></li><li>one</li><li><a href=\"/x\"></a></li><li>two</li></ul>"));
        Assert.Equal("1. a\n2. b", Md("<ol><li>a</li><li> </li><li>b</li></ol>"));
        Assert.Equal("", Md("<ul><li></li></ul>"));
    }

    [Fact]
    public void Emphasis_HugsItsText()
    {
        Assert.Equal("a **bold** and *it* and `code` end", Md("<p>a <b> bold </b> and <em>it</em> and <code>code</code> end</p>"));
        Assert.Equal("plain", Md("<p><b></b>plain<i> </i></p>"));
        Assert.Equal("**once**", Md("<strong><b>once</b></strong>"));
    }

    [Fact]
    public void Pre_IsAFence_WithTheLanguage()
    {
        Assert.Equal("```cs\nvar x = 1;\n  if (a < b) { }\n```", Md("<pre><code class=\"language-cs\">var x = 1;\n  if (a &lt; b) { }</code></pre>"));
        Assert.Equal("```\nplain\n```", Md("<pre>plain</pre>"));
    }

    [Fact]
    public void Tables_AreRows_WithASeparatorAfterTheFirst()
    {
        Assert.Equal("| a | b |\n| --- | --- |\n| 1 | 2 \\| 3 |\n| x | y |", Md("<table><thead><tr><th>a</th><th>b</th></tr></thead><tbody><tr><td>1</td><td>2 | 3</td></tr><tr><td>x</td><td>y</td></tr></tbody></table>"));
    }

    [Fact]
    public void Chrome_IsDropped()
    {
        string html = "<head><script>x()</script><style>p{}</style></head><body><nav><a href=\"/\">Home</a></nav><header><h1>Title</h1></header>" +
                      "<main><p>Body</p><aside>Related</aside><div hidden>hidden</div><span aria-hidden=\"true\">icon</span><div role=\"navigation\">menu</div></main>" +
                      "<form><label>Search</label><input name=q><button>Go</button><select><option>a</option></select></form><footer>© 2026</footer><noscript>enable js</noscript></body>";

        Assert.Equal("# Title\n\nBody\n\nSearch", Md(html));
    }

    [Fact]
    public void Images_KeepTheirAlt_Only()
    {
        Assert.Equal("a [image: A ladybug] b", Md("<p>a <img src=\"l.png\" alt=\"A ladybug\"> b <img src=\"spacer.gif\"></p>"));
    }

    [Fact]
    public void BreaksAndRules()
    {
        Assert.Equal("a\nb\n\nc\n\n---\n\nd", Md("a<br>b<br><br>c<hr>d"));
    }

    [Fact]
    public void DefinitionLists_AndBlockquotes()
    {
        Assert.Equal("term\n  meaning\n\nquoted", Md("<dl><dt>term</dt><dd>meaning</dd></dl><blockquote>quoted</blockquote>"));
    }

    [Fact]
    public void UnclosedTags_AndStrayCloses_AreTolerated()
    {
        Assert.Equal("a\n\nb\n\n- x\n- y", Md("<p>a<p>b</div><ul><li>x<li>y"));
    }

    [Fact]
    public void BlankRuns_CollapseToOneBlankLine()
    {
        Assert.Equal("a\n\nb", Md("<div><div><p>a</p></div></div><div></div><div><p>b</p></div>"));
    }

    [Fact]
    public void Collapse_IsOneLine()
    {
        Assert.Equal("a b c", HtmlToMarkdown.Collapse("  a \n\t b   c  "));
        Assert.Equal("", HtmlToMarkdown.Collapse(" \n "));
    }

    [Fact]
    public void Empty_IsEmpty()
    {
        Assert.Equal("", Md(""));
        Assert.Equal("", Md("<html><head><title>t</title></head><body></body></html>"));
    }
}
