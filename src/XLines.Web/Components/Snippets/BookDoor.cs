using XLines.Data.CrossReference;

namespace XLines.Web.Components.Snippets;

/// <summary>A live price pressed in a book table: the side, the book, and whether Shift was down (a parlay leg).</summary>
public sealed record BookDoor(BestOffer Offer, string Book, bool Shift);
