namespace LineOps.Desk.Primitives;

/// <summary>Where a <c>DeskStep</c> stands in its flow.</summary>
public enum DeskStepState
{
    /// <summary>Not reachable yet: an earlier step is unfinished. Says what it is waiting for.</summary>
    Todo,

    /// <summary>The first unfinished step: the one to do now. Carries its guidance.</summary>
    Current,

    /// <summary>Finished. Its number becomes a check and its guidance steps aside.</summary>
    Done
}
