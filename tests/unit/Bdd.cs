namespace NemoVoiceTyping.Tests;

/// <summary>
/// C# port of our givenpy testing template (taura-2.0/tests/givenpy.py).
/// Import with `using static NemoVoiceTyping.Tests.Bdd;` and structure
/// tests as:
///
///     using (Given("some precondition")) { ... }
///     using (When("the action under test runs")) { ... }
///     using (Then("the expected outcome holds")) { ... }
///
/// Unlike Python, C# scopes variables to their block — declare shared
/// test variables at the top of the method and assign them inside Given.
///
/// Given() optionally takes setup steps, mirroring givenpy's
/// `given([steps])`: each step receives the scope's Context, and any
/// step result that is IDisposable is disposed in reverse order when
/// the Given scope closes (givenpy's __exit__ over started_steps).
/// </summary>
public static class Bdd
{
    /// <summary>Shared bag for setup steps, like givenpy's Context.</summary>
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
