using System.Globalization;
using XLines.Data.Changes;
using XLines.Desk.Windowing;

namespace XLines.Web.Components.Panels;

/// <summary>
/// The change notices a panel hears, read and built in one place: a bare topic (<c>games</c>) or
/// one addressed to an id under it (<c>games:12</c>), as <see cref="DataTopics.Address"/> writes
/// them and <see cref="DeskPanelBase"/> matches them. Several windows made the same choices about
/// them, each with its own copy of the parsing.
/// </summary>
public static class NoticeTokens
{
    /// <summary>The topic of a notice, bare (<c>alerts</c>) or addressed (<c>alerts:12</c>).</summary>
    public static string TopicOf(string token)
    {
        var at = token.IndexOf(DeskSignals.AddressSeparator);
        return at > 0 ? token[..at] : token;
    }

    /// <summary>
    /// Whether a refresh heard of this topic alone, bare or addressed. False for a refresh that
    /// does not say what changed (an empty set), which is taken as everything.
    /// </summary>
    public static bool OnlyOf(IReadOnlySet<string> heard, string topic)
        => heard.Count > 0 && heard.All(token => TopicOf(token) == topic);

    /// <summary>
    /// The ids a refresh heard of, when every token addresses one under <paramref name="topic"/>
    /// and there are no more than <paramref name="max"/>; null when the refresh must be taken as
    /// everything — nothing said what changed, a bare topic or another topic was heard, an id is
    /// not a plain number, or there are too many to ask about.
    /// </summary>
    public static int[]? Addressed(IReadOnlySet<string> heard, string topic, int max)
    {
        if (heard.Count == 0 || heard.Count > max)
            return null;

        var prefix = DataTopics.Address(topic, string.Empty);
        var ids = new int[heard.Count];
        var i = 0;

        foreach (var token in heard)
        {
            if (!token.StartsWith(prefix, StringComparison.Ordinal)
                || !int.TryParse(token.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return null;

            ids[i++] = id;
        }

        return ids;
    }

    /// <summary>
    /// What <see cref="DeskPanelBase"/>'s <c>WatchesIds</c> narrows each of these topics to: these
    /// ids, written as the notices write them. One set is shared by every topic named. Build it
    /// once per load and swap it whole, because the base reads it on the publisher's thread.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Narrow(IEnumerable<int> ids, params string[] topics)
    {
        IReadOnlySet<string> mine = ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        return topics.ToDictionary(topic => topic, _ => mine);
    }
}
