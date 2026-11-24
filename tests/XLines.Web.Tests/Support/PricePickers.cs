using AngleSharp.Dom;
using Bunit;

namespace XLines.Web.Tests.Support;

/// <summary>
/// The shared price picker as a test reaches it: its key, its rows while open, and its Custom
/// price… item, which leads to the window's typed price (the pencil the wager window used to
/// carry beside the book).
/// </summary>
public static class PricePickers
{
    public const string Key = ".price-picker .popup__key";

    public static bool Shown(IRenderedFragment cut) => cut.FindAll(Key).Count > 0;

    public static void Open(IRenderedFragment cut) => cut.Find(Key).Click();

    /// <summary>One radio row per book quoting the side, as drawn while the list is open.</summary>
    public static IReadOnlyList<IElement> Rows(IRenderedFragment cut) => cut.FindAll(".price-picker [role=menuitemradio]");

    /// <summary>Opens the list and chooses Custom price….</summary>
    public static void TypePrice(IRenderedFragment cut)
    {
        Open(cut);
        cut.FindAll(".price-picker [role=menuitem]").First(b => b.TextContent.Contains("Custom price")).Click();
    }
}
