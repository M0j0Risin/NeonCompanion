using System.Drawing;
using NeonSidekick.Diagnostics;
using NeonSidekick.Files;
using PhotoSauce.MagicScaler;
using Spectre.Console;
using Color = Spectre.Console.Color;

namespace NeonSidekick.UI;

/// <summary>
/// The pixels a sent picture is shown as in the transcript, row-major, sized for Spectre's
/// <see cref="Canvas"/>: a cell is one pixel across and two down (the upper-half block, its
/// foreground the top pixel and its background the one below), so <see cref="Columns"/> columns
/// hold as many pixels across and <see cref="MaxRows"/> rows twice as many down. The grid comes
/// from the attachment's own bytes (already at most <see cref="ImageFile.MaxSide"/>) through the
/// same WIC codecs that made them, scaled to fit and never enlarged; transparency is blended
/// over the theme background. Spectre's own image widget lives in its ImageSharp package,
/// which the images feature declined, so this feeds the core <see cref="Canvas"/> by hand. A
/// picture the codecs refuse is simply not drawn.
/// </summary>
public sealed record ImageThumbnail(int Width, int Height, Color[] Pixels)
{
    /// <summary>Cells across the thumbnail, one pixel each.</summary>
    public const int Columns = 48;

    /// <summary>The most rows a thumbnail takes, two pixels each; the aspect ratio picks the height under it.</summary>
    public const int MaxRows = 12;

    public Color At(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return Pixels[y * Width + x];
    }

    /// <summary>
    /// The attachment's bytes decoded and scaled to fit <paramref name="columns"/> pixels across
    /// and <paramref name="maxRows"/> × 2 down, or null when the codecs refuse them (logged at Trace,
    /// never thrown: the picture was already sent, the thumbnail is a courtesy).
    /// </summary>
    public static ImageThumbnail? Read(ImageAttachment image, int columns = Columns, int maxRows = MaxRows)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        try
        {
            var settings = new ProcessImageSettings
            {
                Width = columns,
                Height = maxRows * 2,
                ResizeMode = CropScaleMode.Max,
            };

            using var source = new MemoryStream(image.Bytes, writable: false);
            using var pipeline = MagicImageProcessor.BuildPipeline(source, settings);

            // The pipeline hands back the source's own layout (a conversion transform is not
            // honoured for these), so the bytes are read by the format it reports.
            var pixels = pipeline.PixelSource;
            int width = pixels.Width;
            int height = pixels.Height;
            int bytesPerPixel = BytesPerPixel(pixels.Format);
            if (width < 1 || height < 1 || bytesPerPixel == 0)
            {
                DiagnosticLog.Trace("Image", $"No thumbnail for {image.Path}: {width}x{height}, pixel format {pixels.Format}.");
                return null;
            }

            int stride = width * bytesPerPixel;
            var buffer = new byte[stride * height];
            pixels.CopyPixels(new Rectangle(0, 0, width, height), stride, buffer);
            return new ImageThumbnail(width, height, Decode(buffer, bytesPerPixel, Theme.Bg));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            DiagnosticLog.Trace("Image", $"No thumbnail for {image.Path}: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    /// <summary>Bytes per pixel of the formats read here: 3 for BGR, 4 for BGRA, 1 for grey; 0 for anything else.</summary>
    public static int BytesPerPixel(Guid format)
    {
        if (format == PixelFormats.Bgr24bpp)
        {
            return 3;
        }

        if (format == PixelFormats.Bgra32bpp)
        {
            return 4;
        }

        return format == PixelFormats.Grey8bpp ? 1 : 0;
    }

    /// <summary>
    /// Packed pixels to colours: BGR as they are, grey spread over the channels, BGRA (straight
    /// alpha) blended over <paramref name="matte"/> — the transcript's background, so a transparent
    /// corner looks like the screen behind it. Pure.
    /// </summary>
    public static Color[] Decode(ReadOnlySpan<byte> bytes, int bytesPerPixel, Color matte)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bytesPerPixel, 1);
        var colors = new Color[bytes.Length / bytesPerPixel];
        for (int i = 0; i < colors.Length; i++)
        {
            int at = i * bytesPerPixel;
            colors[i] = bytesPerPixel switch
            {
                1 => new Color(bytes[at], bytes[at], bytes[at]),
                3 => new Color(bytes[at + 2], bytes[at + 1], bytes[at]),
                _ => Over(bytes[at + 2], bytes[at + 1], bytes[at], bytes[at + 3], matte),
            };
        }

        return colors;
    }

    private static Color Over(byte r, byte g, byte b, byte alpha, Color matte)
    {
        if (alpha == 255)
        {
            return new Color(r, g, b);
        }

        if (alpha == 0)
        {
            return matte;
        }

        return new Color(Blend(r, matte.R, alpha), Blend(g, matte.G, alpha), Blend(b, matte.B, alpha));
    }

    private static byte Blend(byte over, byte under, byte alpha) => (byte)((over * alpha + under * (255 - alpha) + 127) / 255);

    /// <summary>The grid as Spectre's <see cref="Canvas"/> at its own size (no rescale to the console's width).</summary>
    public Canvas ToCanvas()
    {
        var canvas = new Canvas(Width, Height) { Scale = false };
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                canvas.SetPixel(x, y, Pixels[y * Width + x]);
            }
        }

        return canvas;
    }
}
