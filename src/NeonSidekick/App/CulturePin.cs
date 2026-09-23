using System.Globalization;

namespace NeonSidekick.App;

/// <summary>
/// The process's cultures pinned to invariant (2026-09-23), the first thing <c>Program.cs</c> does. Until then
/// <c>InvariantGlobalization</c> made every culture invariant; SqlClient refuses that mode, so it is off (the csproj
/// note), and this keeps what the flag gave: a format or parse that forgot <see cref="CultureInfo.InvariantCulture"/>
/// prints on a German or Turkish machine what it prints on the build machine. The rule to pass the invariant
/// culture explicitly stands; this is the net under it. The smoke check <c>culture:invariant</c> and a test pin it.
/// </summary>
public static class CulturePin
{
    /// <summary>Sets the default culture of every thread, and this thread's, to <see cref="CultureInfo.InvariantCulture"/>.</summary>
    public static void Apply()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    /// <summary>Whether <see cref="Apply"/> holds on the calling thread.</summary>
    public static bool Holds =>
        ReferenceEquals(CultureInfo.CurrentCulture, CultureInfo.InvariantCulture) && ReferenceEquals(CultureInfo.CurrentUICulture, CultureInfo.InvariantCulture);
}
