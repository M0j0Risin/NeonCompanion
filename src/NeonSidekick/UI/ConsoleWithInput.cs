using Spectre.Console;
using Spectre.Console.Rendering;

namespace NeonSidekick.UI;

/// <summary>
/// An <see cref="IAnsiConsole"/> that renders to another console but reads keys from a different
/// <see cref="IAnsiConsoleInput"/>. Spectre's prompts read <c>console.Input</c> directly; showing
/// them on this wrapper over a <see cref="KeySource"/> lets a menu see keys that were typed ahead
/// while a reply was streaming instead of skipping them.
/// </summary>
public sealed class ConsoleWithInput : IAnsiConsole
{
    private readonly IAnsiConsole _inner;

    public ConsoleWithInput(IAnsiConsole inner, IAnsiConsoleInput input)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Input = input ?? throw new ArgumentNullException(nameof(input));
    }

    /// <summary>The console this one renders to (a <see cref="ScreenPane"/> in the chat screen).</summary>
    public IAnsiConsole Inner => _inner;

    public Profile Profile => _inner.Profile;

    public IAnsiConsoleCursor Cursor => _inner.Cursor;

    public IAnsiConsoleInput Input { get; }

    public IExclusivityMode ExclusivityMode => _inner.ExclusivityMode;

    public RenderPipeline Pipeline => _inner.Pipeline;

    public void Clear(bool home) => _inner.Clear(home);

    public void Write(IRenderable renderable) => _inner.Write(renderable);

    public void WriteAnsi(Action<AnsiWriter> write) => _inner.WriteAnsi(write);
}
