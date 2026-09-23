using NeonSidekick.Diagnostics;
using NeonSidekick.Settings;

namespace NeonSidekick.UI;

/// <summary>The box a thumbnail is scaled to fit: the two arguments of <see cref="ImageThumbnail.Read"/>.</summary>
public readonly record struct ThumbnailBox(int Columns, int MaxRows);

/// <summary>
/// The thumbnail-size setting: the four words the operator picks from (<c>small</c>, <c>medium</c>,
/// <c>large</c>, <c>xlarge</c>) and their boxes, the way <see cref="Llm.CompactType"/> maps the compact words.
/// <c>small</c> is <see cref="ImageThumbnail"/>'s own default. <see cref="Resolve"/> is the one place
/// the saved string becomes a box: a hand-edited value that is none of them falls back to
/// <see cref="Default"/> with a warning.
/// </summary>
public static class ThumbnailSize
{
    /// <summary>The compiled default, pinned by <c>AppSettingsTests</c>.</summary>
    public const string Default = "small";

    /// <summary>The sizes in menu order.</summary>
    public static readonly string[] Names = { "small", "medium", "large", "xlarge" };

    /// <summary>48 × 12 rows: the thumbnail as it was before the setting.</summary>
    public static readonly ThumbnailBox Small = new(ImageThumbnail.Columns, ImageThumbnail.MaxRows);

    /// <summary>64 × 16 rows.</summary>
    public static readonly ThumbnailBox Medium = new(64, 16);

    /// <summary>80 × 20 rows: fills an 80-column window.</summary>
    public static readonly ThumbnailBox Large = new(80, 20);

    /// <summary>96 × 24 rows: most of a 120 × 30 window with the pane still on screen.</summary>
    public static readonly ThumbnailBox ExtraLarge = new(96, 24);

    private const string Category = "Image";

    /// <summary>Cells kept free at the right edge of a <see cref="Fit"/> box.</summary>
    public const int FitMargin = 2;

    /// <summary>
    /// The box that fills a window of <paramref name="width"/> × <paramref name="height"/> cells
    /// with <paramref name="reservedRows"/> of them spoken for (the pane's rows): the width less
    /// <see cref="FitMargin"/>, the rows left less one, never under 1 × 1. <c>/view</c>'s box
    /// (2026-09-17): the picture as large as the window allows, <see cref="ImageThumbnail.Read"/>
    /// never enlarging it. Pure.
    /// </summary>
    public static ThumbnailBox Fit(int width, int height, int reservedRows) =>
        new(Math.Max(1, width - FitMargin), Math.Max(1, height - reservedRows - 1));

    /// <summary>Trims and ignores case; false (and <see cref="Small"/>) for anything that is not one of <see cref="Names"/>.</summary>
    public static bool TryParse(string? text, out ThumbnailBox box)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "small": box = Small; return true;
            case "medium": box = Medium; return true;
            case "large": box = Large; return true;
            case "xlarge": box = ExtraLarge; return true;
            default: box = Small; return false;
        }
    }

    /// <summary>The menu hint next to a size. Pinned.</summary>
    public static string Describe(string name) => name switch
    {
        "small" => Dimensions(Small),
        "medium" => Dimensions(Medium),
        "large" => Dimensions(Large),
        "xlarge" => Dimensions(ExtraLarge),
        _ => "",
    };

    /// <summary>The box in force for <paramref name="effective"/>; an unknown saved value warns and uses <see cref="Default"/>.</summary>
    public static ThumbnailBox Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        if (TryParse(effective.ImageThumbnailSize, out var box))
        {
            return box;
        }

        DiagnosticLog.Warn(Category,
            $"{nameof(AppSettingsData.ImageThumbnailSize)}='{effective.ImageThumbnailSize}' is not one of {string.Join(", ", Names)}. Using {Default}.");
        TryParse(Default, out box);
        return box;
    }

    private static string Dimensions(ThumbnailBox box) => $"{box.Columns} columns × {box.MaxRows} rows";
}
