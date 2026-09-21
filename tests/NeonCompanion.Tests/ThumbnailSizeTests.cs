using NeonCompanion.Diagnostics;
using NeonCompanion.Settings;
using NeonCompanion.UI;

namespace NeonCompanion.Tests;

public class ThumbnailSizeTests
{
    [Fact]
    public void Names_ArePinned_InMenuOrder()
    {
        Assert.Equal(new[] { "small", "medium", "large", "xlarge" }, ThumbnailSize.Names);
        Assert.Equal("small", ThumbnailSize.Default);
        Assert.Equal(ThumbnailSize.Default, new AppSettingsData().ImageThumbnailSize);
    }

    [Fact]
    public void Boxes_ArePinned_SmallIsTheThumbnailsOwnDefault()
    {
        Assert.Equal(new ThumbnailBox(ImageThumbnail.Columns, ImageThumbnail.MaxRows), ThumbnailSize.Small);
        Assert.Equal(new ThumbnailBox(48, 12), ThumbnailSize.Small);
        Assert.Equal(new ThumbnailBox(64, 16), ThumbnailSize.Medium);
        Assert.Equal(new ThumbnailBox(80, 20), ThumbnailSize.Large);
        Assert.Equal(new ThumbnailBox(96, 24), ThumbnailSize.ExtraLarge);
    }

    [Fact]
    public void Fit_IsTheWindowLessTheMarginAndTheReservedRows_NeverUnderOneByOne()
    {
        // /view's box (2026-09-17): 240 × 50 with the pane's 6 rows spoken for.
        Assert.Equal(new ThumbnailBox(238, 43), ThumbnailSize.Fit(240, 50, 6));
        Assert.Equal(new ThumbnailBox(78, 23), ThumbnailSize.Fit(80, 24, 0));
        Assert.Equal(new ThumbnailBox(1, 1), ThumbnailSize.Fit(2, 5, 5));
        Assert.Equal(new ThumbnailBox(1, 1), ThumbnailSize.Fit(0, 0, 0));
        Assert.Equal(2, ThumbnailSize.FitMargin);
    }

    [Theory]
    [InlineData("small", 48, 12)]
    [InlineData("medium", 64, 16)]
    [InlineData("large", 80, 20)]
    [InlineData("xlarge", 96, 24)]
    [InlineData("  XLarge ", 96, 24)]
    public void TryParse_TrimsAndIgnoresCase(string text, int columns, int maxRows)
    {
        Assert.True(ThumbnailSize.TryParse(text, out var box));
        Assert.Equal(new ThumbnailBox(columns, maxRows), box);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("huge")]
    [InlineData("48x12")]
    [InlineData("extra large")]
    public void TryParse_RejectsAnythingElse_AndHandsBackSmall(string? text)
    {
        Assert.False(ThumbnailSize.TryParse(text, out var box));
        Assert.Equal(ThumbnailSize.Small, box);
    }

    [Fact]
    public void EverySizeHasAHint()
    {
        foreach (var name in ThumbnailSize.Names)
        {
            Assert.True(ThumbnailSize.TryParse(name, out _));
            Assert.NotEqual("", ThumbnailSize.Describe(name));
        }

        Assert.Equal("48 columns × 12 rows", ThumbnailSize.Describe("small"));
        Assert.Equal("64 columns × 16 rows", ThumbnailSize.Describe("medium"));
        Assert.Equal("80 columns × 20 rows", ThumbnailSize.Describe("large"));
        Assert.Equal("96 columns × 24 rows", ThumbnailSize.Describe("xlarge"));
        Assert.Equal("", ThumbnailSize.Describe("huge"));
    }

    [Fact]
    public void Resolve_MapsTheSavedSize()
    {
        Assert.Equal(ThumbnailSize.Large, ThumbnailSize.Resolve(new AppSettingsData { ImageThumbnailSize = "large" }));
        Assert.Equal(ThumbnailSize.Small, ThumbnailSize.Resolve(new AppSettingsData()));
    }

    [Fact]
    public void Resolve_UnknownSavedValue_WarnsAndUsesTheDefault()
    {
        var warnings = new List<DiagnosticEvent>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == "Image" && e.Level == DiagnosticLevel.Warning) warnings.Add(e); };
        DiagnosticLog.Emitted += capture;
        try
        {
            Assert.Equal(ThumbnailSize.Small, ThumbnailSize.Resolve(new AppSettingsData { ImageThumbnailSize = "huge" }));
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        var warning = Assert.Single(warnings);
        Assert.Contains("ImageThumbnailSize='huge' is not one of small, medium, large, xlarge. Using small.", warning.Message);
    }
}
