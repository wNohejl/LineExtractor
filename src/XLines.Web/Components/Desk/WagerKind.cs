namespace XLines.Web.Components.Desk;

/// <summary>What a <see cref="WagerKey"/> does with a wager.</summary>
public enum WagerKind
{
    /// <summary>Opens the place to make one: the Board row's take, the Place wager window.</summary>
    Place,

    /// <summary>Records it in the Journal.</summary>
    Log,

    /// <summary>Banks it on the slip, to log later or build a parlay.</summary>
    AddToSlip,

    /// <summary>Banks it as a parlay leg.</summary>
    AddLeg
}
