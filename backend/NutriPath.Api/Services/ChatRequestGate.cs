using System.Collections.Concurrent;

namespace NutriPath.Api.Services;

/// <summary>
/// Allows one in-flight assistant request per user (per API instance), so double taps and
/// client retries don't call the AI provider twice for the same question.
/// </summary>
public sealed class ChatRequestGate
{
    private readonly ConcurrentDictionary<Guid, byte> _active = new();

    /// <summary>A handle to dispose when the request finishes, or null if one is already running.</summary>
    public IDisposable? TryEnter(Guid userId) =>
        _active.TryAdd(userId, 0) ? new Release(_active, userId) : null;

    private sealed class Release(ConcurrentDictionary<Guid, byte> active, Guid userId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) active.TryRemove(userId, out _);
        }
    }
}
