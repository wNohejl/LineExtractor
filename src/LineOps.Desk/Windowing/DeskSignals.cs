namespace LineOps.Desk.Windowing;

/// <summary>
/// Tells open windows that the data under them moved.
///
/// <para>
/// A topic is whatever the application names its data by — the desk neither knows nor cares
/// what "orders" or "prices" are. The application publishes (from a database listener, from its
/// own writes); a window subscribes to the topics it draws from and reloads. One hub per
/// process, shared by every circuit, because a change is a fact about the data, not about
/// anyone's session.
/// </para>
///
/// <para>
/// A publication is a set of tokens, each a bare topic (<c>orders</c>: something under it moved)
/// or a topic addressed to one id (<c>orders:4123</c>, see <see cref="Address"/>). A window that
/// watches the topic hears both; one that also names the ids it is about
/// (<see cref="DeskPanelBase.WatchesIds"/>) hears the bare topic and its own ids only, so the
/// window for one order sits still while a dozen others change. A publisher that cannot say which
/// rows moved — a fallback poll, a reconnect, a bulk write — publishes the bare topic, and that
/// still reaches everyone.
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
    /// <summary>Raised with the tokens that changed. Handlers run on the publisher's thread.</summary>
    public event Action<IReadOnlySet<string>>? Changed;

    /// <summary>Raised when <see cref="Live"/> flips.</summary>
    public event Action<bool>? LiveChanged;

    public bool Live { get; private set; }

    /// <summary>Separates a topic from the id it is addressed to: <c>orders:4123</c>.</summary>
    public const char AddressSeparator = ':';

    /// <summary>
    /// The token for a change to one thing under a topic — <c>orders:4123</c> — so a window about
    /// that thing can tell its change from its neighbours'. Ids are strings: the desk does not
    /// know what the application keys its data by.
    /// </summary>
    public static string Address(string topic, string id) => $"{topic}{AddressSeparator}{id}";

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
