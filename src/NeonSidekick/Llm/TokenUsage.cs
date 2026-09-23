using Microsoft.Extensions.AI;

namespace NeonSidekick.Llm;

/// <summary>
/// What one or more model requests cost: the token counts the server reported and the time they
/// took. Pure; the sum of two is the usage of both. <see cref="Zero"/> is the empty tally.
///
/// <para>The two spans are the reply's two phases. <see cref="ToFirstToken"/> is the wait before
/// the first chunk with content (the prefill: the server reading the prompt), <see cref="Generating"/>
/// the time from that chunk to the end of the stream (the decode). <see cref="TokensPerSecond"/>
/// is <see cref="Output"/> over the second, which is the figure every server prints as its
/// generation speed; a reasoning model's thinking tokens are in both the count and the window,
/// and <see cref="Reasoning"/> says how many of them there were when the server counts them.
/// A prompt speed (input over the wait) is deliberately not offered: a local server reuses its
/// KV cache from the second turn on and the figure would be fiction.</para>
/// </summary>
/// <param name="Input">Prompt tokens: the whole context the request carried.</param>
/// <param name="Output">Completion tokens, thinking included.</param>
/// <param name="Total">As the server reported it, or the sum of the two when it did not.</param>
/// <param name="Requests">How many model requests are summed here; zero means nothing was counted.</param>
/// <param name="ToFirstToken">Summed over the requests: the total time spent waiting for the server to start talking.</param>
/// <param name="Generating">Summed over the requests: the total time the server was streaming.</param>
/// <param name="Reasoning">
/// The thinking's share of <see cref="Output"/>, as the server counted it (vLLM, LM Studio and SGLang
/// all count it inside the completion). Null when the report carried no such field; <c>0</c> is a
/// report too (thinking off). A sum stays null only while nothing reported one.
/// </param>
public readonly record struct TokenUsage(long Input, long Output, long Total, int Requests, TimeSpan ToFirstToken, TimeSpan Generating, long? Reasoning = null)
{
    public static readonly TokenUsage Zero = default;

    /// <summary>True when no request was counted.</summary>
    public bool IsEmpty => Requests == 0;

    /// <summary>Completion tokens per second of streaming, or null when nothing streamed or no time passed.</summary>
    public double? TokensPerSecond =>
        Output > 0 && Generating > TimeSpan.Zero ? Output / Generating.TotalSeconds : null;

    /// <summary>
    /// The wait per request: <see cref="ToFirstToken"/> is summed over the requests, so a sum of
    /// many (the conversation, the session) reads as an average this way, the aggregate counterpart
    /// of the last reply's whole wait. Null when nothing was counted.
    /// </summary>
    public TimeSpan? AverageToFirstToken => Requests > 0 ? ToFirstToken / Requests : null;

    /// <summary>
    /// One request's usage as the adapter delivered it. A server may report only some of the
    /// counts; a missing count reads as zero and a missing total as the sum of the two. The
    /// reasoning count stays null when absent — the adapter fills it from the OpenAI shape,
    /// <see cref="OpenAICompatibleChatClient"/> from SGLang's.
    /// </summary>
    public static TokenUsage From(UsageDetails details, TimeSpan toFirstToken, TimeSpan generating)
    {
        ArgumentNullException.ThrowIfNull(details);
        long input = details.InputTokenCount ?? 0;
        long output = details.OutputTokenCount ?? 0;
        return new TokenUsage(input, output, details.TotalTokenCount ?? input + output, 1, toFirstToken, generating, details.ReasoningTokenCount);
    }

    /// <summary>Every count and span summed; the reasoning count is the sum of the reports that carried one, null when neither did.</summary>
    public static TokenUsage operator +(TokenUsage a, TokenUsage b) =>
        new(
            a.Input + b.Input,
            a.Output + b.Output,
            a.Total + b.Total,
            a.Requests + b.Requests,
            a.ToFirstToken + b.ToFirstToken,
            a.Generating + b.Generating,
            a.Reasoning is null && b.Reasoning is null ? null : (a.Reasoning ?? 0) + (b.Reasoning ?? 0));
}
