using System.Diagnostics;
using System.Globalization;
using System.Text;
using NeonSidekick.Diagnostics;

namespace NeonSidekick.Shell;

/// <summary>A child that could not be started: the sentence names the executable and Windows' reason.</summary>
public sealed class ShellStartException(string message) : Exception(message);

/// <summary>
/// The one place the shell tools start a process (2026-09-21) — the fourth counted process-start
/// site after <c>PersonaFile</c>, <c>HeadlessBrowser</c> and the MCP SDK's stdio transport; the code
/// tool and its bridge build a <see cref="ProcessLaunch"/> and come through here too. The
/// <c>HeadlessBrowser.RunAsync</c> shape: no shell-execute, no window (the child gets a hidden
/// console of its own, so the TUI's Ctrl+C — a key here — never reaches it and its own never
/// reaches us), the three streams redirected as UTF-8, the environment from
/// <see cref="ChildEnvironment"/>. Both output pumps start before anything waits (reading one
/// stream to its end before the other deadlocks a child that fills the other pipe), each line
/// landing in the session's <see cref="OutputBuffer"/> tagged by stream; the session completes
/// once the process is gone and both pumps have drained.
/// </summary>
public sealed class ShellRunner
{
    private readonly TimeProvider _time;

    public ShellRunner(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>A fresh id: <paramref name="prefix"/> and six lower hex digits (<c>proc_3f2a1b</c>, <c>run_9c04e1</c>).</summary>
    public static string NewId(Random random, string prefix)
    {
        ArgumentNullException.ThrowIfNull(random);
        return prefix + random.Next(0, 0x1000000).ToString("x6", CultureInfo.InvariantCulture);
    }

    /// <summary>Starts <paramref name="launch"/> as <paramref name="id"/>; the session is the caller's to wait on, kill and dispose.</summary>
    /// <exception cref="ShellStartException">The program could not be started (missing, refused, not a program).</exception>
    public ProcessSession Start(ProcessLaunch launch, string id)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(id);
        var start = new ProcessStartInfo(launch.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = launch.WorkingDirectory,
        };
        if (launch.ArgumentList is { } list)
        {
            foreach (string argument in list)
            {
                start.ArgumentList.Add(argument);
            }
        }
        else
        {
            start.Arguments = launch.Arguments ?? "";
        }

        ChildEnvironment.Apply(start, launch);

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            throw new ShellStartException(ShellText.CouldNotStart(Path.GetFileName(launch.Executable), ex.Message));
        }

        if (process is null)
        {
            throw new ShellStartException(ShellText.CouldNotStart(Path.GetFileName(launch.Executable), "no process"));
        }

        var session = new ProcessSession(id, launch, process, _time);
        DiagnosticLog.Debug(ShellKinds.Category, $"{id}: started pid {process.Id.ToString(CultureInfo.InvariantCulture)} ({launch.Kind}) {launch.Label}");
        _ = PumpAndCompleteAsync(session, process);
        return session;
    }

    private static async Task PumpAndCompleteAsync(ProcessSession session, Process process)
    {
        var stdout = PumpAsync(process.StandardOutput, session.Output, isError: false);
        var stderr = PumpAsync(process.StandardError, session.Output, isError: true);
        int code;
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            code = process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        {
            // Disposed under us (the app's exit): the code is whatever a killed child gives.
            code = -1;
        }

        DiagnosticLog.Debug(ShellKinds.Category, $"{session.Id}: exit {code.ToString(CultureInfo.InvariantCulture)} after {session.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s, {session.Output.TotalLines.ToString(CultureInfo.InvariantCulture)} lines");
        session.Complete(code);
    }

    private static async Task PumpAsync(StreamReader reader, OutputBuffer output, bool isError)
    {
        try
        {
            while (await reader.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            {
                output.Append(line, isError);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The pipe went with the process (a kill, a dispose): what arrived is kept.
        }
    }
}
