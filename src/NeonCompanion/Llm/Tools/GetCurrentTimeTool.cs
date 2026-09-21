using System.Text.Json;
using Microsoft.Extensions.AI;

namespace NeonCompanion.Llm.Tools;

/// <summary>
/// <c>get_current_time(zone?)</c>: the machine's clock, local or in a named zone, as one sentence
/// (<see cref="ClockText.FormatMoment"/> / <see cref="ClockText.FormatElsewhere"/>). The model
/// does not know the date; the system prompt tells it to call this whenever a question depends
/// on it. Reads the clock through <see cref="TimeProvider"/> so a test fixes "now".
/// </summary>
public sealed class GetCurrentTimeTool : AIFunction
{
    public const string ToolName = "get_current_time";

    public const string ZoneArgument = "zone";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "zone": {
              "type": "string",
              "description": "An IANA time zone such as Europe/Paris or Asia/Tokyo. Leave it out for the user's local time."
            }
          }
        }
        """);

    private readonly TimeProvider _time;

    public GetCurrentTimeTool(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public override string Name => ToolName;

    public override string Description =>
        "The current date, time, weekday and time zone. Call it whenever a question depends on today's date or the time now.";

    public override JsonElement JsonSchema => Schema;

    /// <summary>The sentence for a zone argument (blank = local), pinned; an unknown zone is an <c>Error:</c> sentence.</summary>
    public string Describe(string zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var utc = _time.GetUtcNow();
        var local = ClockText.LocalNow(_time);
        if (string.IsNullOrWhiteSpace(zone))
        {
            return ClockText.FormatMoment(local, _time.LocalTimeZone);
        }

        if (!ClockText.TryFindZone(zone, out var there))
        {
            return ClockText.UnknownZone(zone);
        }

        return ClockText.FormatElsewhere(TimeZoneInfo.ConvertTime(utc, there), zone.Trim(), local);
    }

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new ValueTask<object?>(Describe(ToolArguments.ReadString(arguments, ZoneArgument)));
    }
}
