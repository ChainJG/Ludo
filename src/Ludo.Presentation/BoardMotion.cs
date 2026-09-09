using System.Collections.Immutable;
using Ludo.Engine;

namespace Ludo.Presentation;

public sealed record TokenMotion(string Key, ImmutableArray<BoardPoint> Points, bool Captured);
public sealed record BoardMotion(ImmutableArray<TokenMotion> Moves)
{
    public static BoardMotion Between(GameStateView before, GameStateView after)
    {
        var previous = BoardPresentation.Tokens(before).ToDictionary(t => (t.Player, t.Token));
        var next = BoardPresentation.Tokens(after).ToDictionary(t => (t.Player, t.Token));
        var moves = ImmutableArray.CreateBuilder<TokenMotion>();
        for (int player = 0; player < after.Players.Length; player++)
        for (int token = 0; token < 4; token++)
        {
            int from = before.Players[player].Tokens[token], to = after.Players[player].Tokens[token];
            if (from == to) continue;
            var start = previous[(player, token)]; var end = next[(player, token)];
            var points = ImmutableArray.CreateBuilder<BoardPoint>();
            points.Add(new(start.X, start.Y));
            bool captured = to == GameEngine.Base && from >= 0;
            if (captured)
                for (int progress = from - 1; progress >= 0; progress--) points.Add(Percent(player, token, progress));
            else if (from >= 0)
                for (int progress = from + 1; progress < to; progress++) points.Add(Percent(player, token, progress));
            points.Add(new(end.X, end.Y));
            moves.Add(new($"{player}-{token}", points.ToImmutable(), captured));
        }
        return new(moves.OrderBy(m => m.Captured).ToImmutableArray());

        BoardPoint Percent(int player, int token, int progress)
        {
            var point = BoardPresentation.Location(after.Players[player].Seat, token, progress);
            return BoardPresentation.ToPercent(point);
        }
    }
}
