namespace NemoVoiceTyping.Tests;

/// <summary>
/// Given/When/Then test template. Import with
/// <c>using static NemoVoiceTyping.Tests.GivenWhenThen;</c> and structure
/// tests as <c>using (Given(...)) { } using (When(...)) { } using (Then(...)) { }</c>.
/// Given() optionally runs setup steps; step results that are IDisposable
/// are disposed in reverse order when the scope closes.
/// </summary>
public static class GivenWhenThen
{
    public sealed class Context
    {
        private readonly Dictionary<string, object?> _bag = new();

        public object? this[string key]
        {
            get => _bag.TryGetValue(key, out var value) ? value : null;
            set => _bag[key] = value;
        }
    }

    public sealed class GivenScope : IDisposable
    {
        public Context Context { get; } = new();
        private readonly List<IDisposable> _started = new();

        internal GivenScope(Func<Context, object?>[] steps)
        {
            foreach (var step in steps)
            {
                if (step(Context) is IDisposable started) _started.Add(started);
            }
        }

        public void Dispose()
        {
            for (var i = _started.Count - 1; i >= 0; i--) _started[i].Dispose();
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    public static GivenScope Given(string? description = null,
        params Func<Context, object?>[] steps) => new(steps);

    public static IDisposable When(string? description = null) => NullScope.Instance;

    public static IDisposable Then(string? description = null) => NullScope.Instance;

    public static IDisposable But(string? description = null) => NullScope.Instance;
}
