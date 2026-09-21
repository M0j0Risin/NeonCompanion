namespace NeonCompanion.UI;

/// <summary>
/// The <c>T</c> of a <see cref="Spectre.Console.SelectionPrompt{T}"/> that must offer "ESC = cancel".
///
/// <para>Spectre's prompt is constrained <c>where T : notnull</c>, so signalling cancel with a
/// <c>null</c> type argument (<c>SelectionPrompt&lt;T?&gt;</c>) violates the constraint
/// (CS8714 / CS8621 / CS8622). Wrapping each choice keeps the generic notnull and encodes cancel
/// as an in-band value: a picker builds choices with <see cref="From"/>, wires
/// <c>AddCancelResult</c> to <see cref="Canceled"/>, and checks <see cref="IsCanceled"/>.</para>
/// </summary>
/// <typeparam name="T">The picked choice type.</typeparam>
public sealed record PromptResult<T> where T : notnull
{
    /// <summary>The picked value, or <c>null</c> when the pick was cancelled.</summary>
    public T? Value { get; init; }

    /// <summary>True when the user cancelled the pick (ESC).</summary>
    public bool IsCanceled { get; init; }

    /// <summary>The shared cancel sentinel; one instance per <typeparamref name="T"/>.</summary>
    public static PromptResult<T> Canceled { get; } = new() { IsCanceled = true };

    /// <summary>Wraps a picked value as a not-cancelled result.</summary>
    public static PromptResult<T> From(T value) => new() { Value = value ?? throw new ArgumentNullException(nameof(value)) };
}
