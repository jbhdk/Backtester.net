namespace Backtester.Core
{
    /// <summary>
    /// Typed metadata container for strategy-specific per-position state.
    /// </summary>
    /// <remarks>
    /// Public by reachability, not by allowlist (ADR 0034): it is the type of
    /// <see cref="Position.Metadata"/>, a member of a boundary DTO the engine hands to a strategy,
    /// so narrowing it would take that property internal with it.
    /// </remarks>
    public class PositionMetadata
    {
    }
}
