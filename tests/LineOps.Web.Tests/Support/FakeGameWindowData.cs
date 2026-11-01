using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Web.Services;

namespace LineOps.Web.Tests.Support;

/// <summary>
/// The Game window's data seam, in memory: one row, the game alone as the row holds it, and an
/// empty rest — counting each read, so a test can say which part of the window a notice reloaded.
/// </summary>
public sealed class FakeGameWindowData : IGameWindowData
{
    public BoardRow? Row { get; set; } = BoardRows.Row();

    /// <summary>What <see cref="GameAsync"/> answers; the row's own game unless set.</summary>
    public Game? Game { get; set; }

    /// <summary>What <see cref="DetailAsync"/> answers; an empty detail unless set.</summary>
    public GameWindowDetail Detail { get; set; } = GameWindowDetail.Empty;

    public int RowReads { get; private set; }
    public int GameReads { get; private set; }
    public int DetailReads { get; private set; }

    public Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default)
    {
        RowReads++;
        return Task.FromResult(Game is { } game && Row is { } row ? row with { Game = game } : Row);
    }

    public Task<Game?> GameAsync(int gameId, CancellationToken ct = default)
    {
        GameReads++;
        return Task.FromResult(Game ?? Row?.Game);
    }

    public Task<GameWindowDetail> DetailAsync(Game game, int? seasonYear, bool withBets, CancellationToken ct = default)
    {
        DetailReads++;
        return Task.FromResult(Detail);
    }
}
