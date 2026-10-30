namespace LineOps.Desk.Primitives;

/// <summary>One advisory row: a state, one sentence, and optionally a remedy key on the same row.</summary>
public sealed record Check(DeskState State, string Text, string? Remedy = null, Func<Task>? Act = null);
