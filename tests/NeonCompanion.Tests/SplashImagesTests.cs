using System.Reflection;
using NeonCompanion.Files;
using NeonCompanion.UI;

namespace NeonCompanion.Tests;

public class SplashImagesTests
{
    [Fact]
    public void Names_AreTheEmbeddedPictures_UnderThePrefix_InOrdinalOrder()
    {
        var names = SplashImages.Names;

        // The repo's assets\splash rides in the exe (the csproj glob): at least the two of 2026-09-18.
        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.StartsWith(SplashImages.ResourcePrefix, n));
        Assert.All(names, n => Assert.True(ImageFile.IsImagePath(n), n));
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.Equal("splash/", SplashImages.ResourcePrefix);
        Assert.Contains("splash/splash_01.png", names);   // the zero-padded names since 2026-09-18
    }

    [Fact]
    public void FromDirectory_IsTheFoldersPictures_InNameOrder_LoadedFromDisk_OrNull()
    {
        // The profile's own splash folder (later on 2026-09-19): image files alone, ordinal order; null without one.
        string dir = Path.Combine(Path.GetTempPath(), "NeonCompanion.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(SplashImages.FromDirectory(dir));   // missing
            Directory.CreateDirectory(dir);
            Assert.Null(SplashImages.FromDirectory(dir));   // empty
            File.WriteAllText(Path.Combine(dir, "prompt.txt"), "notes");
            Assert.Null(SplashImages.FromDirectory(dir));   // no picture
            File.WriteAllBytes(Path.Combine(dir, "b.bmp"), App.SmokeChecks.SolidBmp(4, 2));
            File.WriteAllBytes(Path.Combine(dir, "A.BMP"), App.SmokeChecks.SolidBmp(6, 3));
            File.WriteAllText(Path.Combine(dir, "broken.png"), "not a picture");

            var source = SplashImages.FromDirectory(dir);

            Assert.NotNull(source);
            Assert.Equal(["A.BMP", "b.bmp", "broken.png"], source.Names);
            var a = source.Load("A.BMP");
            Assert.NotNull(a);
            Assert.Equal((6, 3), (a.Width, a.Height));
            Assert.Null(source.Load("broken.png"));   // the codecs refuse it: nothing drawn, never a throw
            Assert.Null(source.Load("missing.bmp"));
            Assert.Equal("splash", SplashImages.ProfileFolderName);
            Assert.Equal("Splash: 1 picture from " + dir, SplashImages.FolderLogLine(1, dir));
            Assert.Equal("Splash: 3 pictures from " + dir, SplashImages.FolderLogLine(3, dir));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ReadNames_SkipsEverythingOutsideThePrefix_AndNonImages()
    {
        // The test assembly embeds nothing under splash/; the app's set is what Names reads.
        Assert.Empty(SplashImages.ReadNames(typeof(SplashImagesTests).Assembly));
        Assert.Equal(SplashImages.Names, SplashImages.ReadNames(typeof(SplashImages).Assembly));
        Assert.DoesNotContain(typeof(SplashImages).Assembly.GetManifestResourceNames(), n => n.StartsWith(SplashImages.ResourcePrefix, StringComparison.Ordinal) && !ImageFile.IsImagePath(n));
    }

    [Fact]
    public void EveryPicture_Loads_AndReadsToAThumbnail()
    {
        foreach (var name in SplashImages.Names)
        {
            var image = SplashImages.Load(name);

            Assert.NotNull(image);
            Assert.Equal(name, image.Path);
            Assert.True(image.Width > 0 && image.Height > 0, name);
            Assert.True(image.Width <= ImageFile.MaxSide && image.Height <= ImageFile.MaxSide, name);
            var thumbnail = ImageThumbnail.Read(image, 238, 33);
            Assert.NotNull(thumbnail);
            Assert.True(thumbnail.Width <= 238 && thumbnail.Height <= 66, name);
        }
    }

    [Fact]
    public void Pick_IsSeeded_InTheList_AndNullOverNothing()
    {
        string[] names = ["splash/a.png", "splash/b.png", "splash/c.png"];

        var first = SplashImages.Pick(new Random(7), names);
        var again = SplashImages.Pick(new Random(7), names);

        Assert.NotNull(first);
        Assert.Contains(first, names);
        Assert.Equal(first, again);
        Assert.Null(SplashImages.Pick(new Random(7), []));
        // Every picture comes up over enough draws: the pick is over the whole list.
        var seen = Enumerable.Range(0, 200).Select(i => SplashImages.Pick(new Random(i), names)).Distinct().Order().ToArray();
        Assert.Equal(names, seen);
        Assert.Throws<ArgumentNullException>(() => SplashImages.Pick(null!, names));
    }

    [Fact]
    public void Load_AMissingName_IsNull_NeverAThrow()
    {
        Assert.Null(SplashImages.Load("splash/missing.png"));
        Assert.Throws<ArgumentException>(() => SplashImages.Load(""));
        Assert.Throws<ArgumentNullException>(() => SplashImages.Load(null!));
    }

    [Fact]
    public void Source_IsTheNamesAndTheLoader()
    {
        // The screen's seam (2026-09-19): the embedded names over Load.
        var source = SplashImages.Source;

        Assert.Same(SplashImages.Names, source.Names);
        var image = source.Load(source.Names[0]);
        Assert.NotNull(image);
        Assert.Equal(source.Names[0], image.Path);
        Assert.Null(source.Load("splash/missing.png"));
    }

    [Fact]
    public void Next_WrapsBothWays_OneNameIsItself_AnUnknownCurrentIsTheFirst_NothingOverNothing()
    {
        // Left / Right at the empty line (2026-09-19): the neighbour in name order, the ends joined.
        string[] names = ["a", "b", "c"];
        Assert.Equal("c", SplashImages.Next(names, "b", +1));
        Assert.Equal("a", SplashImages.Next(names, "c", +1));
        Assert.Equal("a", SplashImages.Next(names, "b", -1));
        Assert.Equal("c", SplashImages.Next(names, "a", -1));
        Assert.Equal("a", SplashImages.Next(["a"], "a", +1));
        Assert.Equal("a", SplashImages.Next(["a"], "a", -1));
        Assert.Equal("a", SplashImages.Next(names, null, +1));
        Assert.Equal("a", SplashImages.Next(names, "z", -1));
        Assert.Null(SplashImages.Next([], "a", +1));
        Assert.Throws<ArgumentNullException>(() => SplashImages.Next(null!, "a", +1));
    }
}
