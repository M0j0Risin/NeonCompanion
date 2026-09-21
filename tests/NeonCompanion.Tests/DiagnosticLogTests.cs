using NeonCompanion.Diagnostics;

namespace NeonCompanion.Tests;

/// <summary>
/// DiagnosticLog is static, so these tests filter by a unique category and always unsubscribe;
/// they must stay correct when other test classes emit concurrently.
/// </summary>
public class DiagnosticLogTests
{
    [Fact]
    public void Emitted_CarriesLevelCategoryMessageAndException()
    {
        string category = Guid.NewGuid().ToString("N");
        DiagnosticEvent? seen = null;
        Action<DiagnosticEvent> handler = e => { if (e.Category == category) seen = e; };
        DiagnosticLog.Emitted += handler;
        bool echo = DiagnosticLog.EchoToConsole;
        DiagnosticLog.EchoToConsole = false;
        try
        {
            var ex = new InvalidOperationException("boom");
            DiagnosticLog.Error(category, "it broke", ex);
        }
        finally
        {
            DiagnosticLog.Emitted -= handler;
            DiagnosticLog.EchoToConsole = echo;
        }

        Assert.NotNull(seen);
        Assert.Equal(DiagnosticLevel.Error, seen.Value.Level);
        Assert.Equal("it broke", seen.Value.Message);
        Assert.IsType<InvalidOperationException>(seen.Value.Exception);
        Assert.True(seen.Value.TimestampUtc <= DateTime.UtcNow);
    }

    [Fact]
    public void Emitted_ThrowingSubscriberDoesNotStarveTheOthersOrTheCaller()
    {
        string category = Guid.NewGuid().ToString("N");
        int received = 0;
        Action<DiagnosticEvent> bad = e => { if (e.Category == category) throw new Exception("bad subscriber"); };
        Action<DiagnosticEvent> good = e => { if (e.Category == category) received++; };
        DiagnosticLog.Emitted += bad;
        DiagnosticLog.Emitted += good;
        bool echo = DiagnosticLog.EchoToConsole;
        DiagnosticLog.EchoToConsole = false;
        try
        {
            var caught = Record.Exception(() => DiagnosticLog.Info(category, "hello"));
            Assert.Null(caught);
        }
        finally
        {
            DiagnosticLog.Emitted -= bad;
            DiagnosticLog.Emitted -= good;
            DiagnosticLog.EchoToConsole = echo;
        }

        Assert.Equal(1, received);
    }

    [Fact]
    public void Write_WithNoSubscribers_DoesNotThrow()
    {
        bool echo = DiagnosticLog.EchoToConsole;
        DiagnosticLog.EchoToConsole = false;
        try
        {
            DiagnosticLog.Trace(Guid.NewGuid().ToString("N"), "nobody listening");
        }
        finally
        {
            DiagnosticLog.EchoToConsole = echo;
        }
    }
}
