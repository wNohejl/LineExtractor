namespace LineOps.Desk.Windowing;

/// <summary>
/// Tells open windows that the data under them moved.
///
/// <para>
/// A topic is whatever the application names its data by — the desk neither knows nor cares
/// what "games" or "odds" are. The application publishes (from a database listener, from its
/// own writes); a window subscribes to the topics it draws from and reloads. One hub per
/// process, shared by every circuit, because a change is a fact about the data, not about
/// anyone's session.
/// </para>
///
/// <para>
/// <see cref="Live"/> says whether publications are reaching the hub as they happen. When the
/// application's feed is down it publishes on a slow clock instead, and a window can say that it
/// is polling rather than live.
/// </para>
/// </summary>
public sealed class DeskSignals
{
    /// <summary>Raised with the topics that changed. Handlers run on the publisher's thread.</summary>
    public event Action<IReadOnlySet<string>>? Changed;

    /// <summary>Raised when <see cref="Live"/> flips.</summary>
    public event Action<bool>? LiveChanged;

    public bool Live { get; private set; }

    public void Publish(IReadOnlySet<string> topics)
    {
        if (topics.Count > 0)
            Changed?.Invoke(topics);
    }

    public void SetLive(bool live)
    {
        if (live == Live)
            return;

        Live = live;
        LiveChanged?.Invoke(live);
    }
}
