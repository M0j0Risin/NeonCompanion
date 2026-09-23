using System.Runtime.CompilerServices;

namespace NeonCompanion.Tests;

/// <summary>
/// The test process runs with the app's cultures (2026-09-23): <c>InvariantGlobalization</c> is off in both
/// projects since SqlClient refuses it, and <c>Program.cs</c> pins every culture to invariant first thing —
/// so does this, before any test runs, or a machine's own culture would leak into formats the app never sees.
/// </summary>
internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void PinCultures() => App.CulturePin.Apply();
}
