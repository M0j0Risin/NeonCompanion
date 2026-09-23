using System.Globalization;

namespace NeonSidekick.UI;

/// <summary>The clock shapes the hint row draws: a timer's remaining time and a spinner's elapsed time share one.</summary>
public static class ElapsedText
{
    /// <summary>
    /// The countdown form: <c>09:27</c>, <c>00:45</c>, <c>03:10:15</c> once an hour is in it — two
    /// digits per unit, the hours only when there are any; whole seconds. Pinned.
    /// </summary>
    public static string Countdown(TimeSpan duration)
    {
        long total = (long)Math.Round(duration.TotalSeconds);
        long h = total / 3600, m = total % 3600 / 60, s = total % 60;
        string tail = Two(m) + ":" + Two(s);
        return h > 0 ? Two(h) + ":" + tail : tail;
    }

    private static string Two(long n) => n.ToString("00", CultureInfo.InvariantCulture);
}
