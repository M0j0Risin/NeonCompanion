using System.Globalization;
using System.Text;

namespace NeonSidekick.Diagnostics;

/// <summary>
/// The record of a crash: the exception that took the app down, with its stack, appended to
/// <see cref="FileName"/> under the settings directory (<c>%USERPROFILE%\.neonsidekick\crash.log</c>).
///
/// <para>The terminal window closes with the process, so an unhandled exception's stderr print is
/// never read; <c>--log</c> writes an exception's type and message only, never a stack. This file
/// is the one place the stack lands. The writer never throws — a crash handler that throws hides
/// the crash — and every sentence is pinned.</para>
/// </summary>
public static class CrashReport
{
    /// <summary>The file's name under the settings directory.</summary>
    public const string FileName = "crash.log";

    /// <summary>The diagnostic category of the crash lines.</summary>
    public const string Category = "App";

    /// <summary>
    /// Appends <paramref name="exception"/> (its type, message, stack and inner exceptions, as
    /// <see cref="Exception.ToString"/> gives them) under a dated header to <see cref="FileName"/> in
    /// <paramref name="directory"/>, creating both. Returns the file's path, or null when the write
    /// itself failed — nothing is thrown.
    /// </summary>
    public static string? Write(string directory, Exception exception, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, FileName);
            File.AppendAllText(path, Entry(exception, utcNow), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>One entry of the file: <c>--- yyyy-MM-dd HH:mm:ss.fff crash ---</c>, the exception, a blank line. Invariant. Pinned.</summary>
    public static string Entry(Exception exception, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return string.Create(CultureInfo.InvariantCulture, $"--- {utcNow:yyyy-MM-dd HH:mm:ss.fff} crash ---{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
    }

    /// <summary>The line printed on the way out. Pinned.</summary>
    public static string Notice(string? path, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string where = path is null ? "Details could not be written." : $"Details in {path}.";
        return $"NeonSidekick crashed: {exception.GetType().Name}: {exception.Message}. {where}";
    }

    /// <summary>The <c>--log</c> line pointing at the file. Pinned.</summary>
    public static string LogLine(string? path) => path is null ? "Crashed; the crash report could not be written." : $"Crashed; details in {path}";
}
