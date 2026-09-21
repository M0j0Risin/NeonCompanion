using System.Text;
using NeonCompanion.Web;

namespace NeonCompanion.Tests;

public class PageCharsetTests
{
    [Fact]
    public void HeaderCharset_Wins()
    {
        var bytes = Encoding.Latin1.GetBytes("<meta charset=\"utf-8\">café");

        Assert.Equal("<meta charset=\"utf-8\">café", PageCharset.Decode(bytes, "iso-8859-1"));
    }

    [Fact]
    public void Bom_IsHonoured_AndStripped()
    {
        Assert.Equal("héllo", PageCharset.Decode(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("héllo")).ToArray(), null));
        Assert.Equal("héllo", PageCharset.Decode(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("héllo")).ToArray(), null));
    }

    [Fact]
    public void MetaCharset_IsSniffed_Windows1252Included()
    {
        var page = PageCharset.Lookup("windows-1252")!.GetBytes("<html><head><meta charset=windows-1252></head><body>café — “q”</body></html>");

        Assert.Equal("<html><head><meta charset=windows-1252></head><body>café — “q”</body></html>", PageCharset.Decode(page, null));
        Assert.Equal("windows-1252", PageCharset.Sniff("<meta charset=windows-1252>"));
        Assert.Equal("ISO-8859-1", PageCharset.Sniff("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=ISO-8859-1\">"));
        Assert.Null(PageCharset.Sniff("<meta name=viewport>"));
    }

    [Fact]
    public void UnknownOrMissing_IsUtf8()
    {
        Assert.Equal("héllo", PageCharset.Decode(Encoding.UTF8.GetBytes("héllo"), "x-made-up"));
        Assert.Equal("héllo", PageCharset.Decode(Encoding.UTF8.GetBytes("héllo"), null));
        Assert.Null(PageCharset.Lookup("x-made-up"));
        Assert.Null(PageCharset.Lookup(""));
        Assert.Equal("utf-8", PageCharset.Lookup("\"UTF-8\"")!.WebName);
    }
}
