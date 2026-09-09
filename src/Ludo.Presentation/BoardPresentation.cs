using System.Collections.Immutable;
using Ludo.Engine;

namespace Ludo.Presentation;

public readonly record struct BoardPoint(double X, double Y);
public sealed record TokenVisual(int Player, int Token, int Seat, double X, double Y, string Description);

public static class BoardPresentation
{
    public static readonly string[] Colours = ["Yellow", "Blue", "Red", "Green"];
    public static readonly string[] HexColours = ["#F4C541", "#39A6F5", "#FF6575", "#44D59C"];
    // Measured grid lines in the supplied 1254px artwork. Its rows are not evenly spaced.
    private static readonly double[] Columns = [37,115.5,194.5,272.5,351,429,506.5,586,667,746.5,824,902.5,981.5,1059.5,1138,1216.5];
    private static readonly double[] Rows = [38,119,197.5,275,353,430,489,567,647,721.5,795,869,946,1024,1101,1202];
    public static BoardPoint ToPercent(BoardPoint point) => new(Interpolate(Columns, point.X) / 12.54, Interpolate(Rows, point.Y) / 12.54);
    private static double Interpolate(double[] lines, double value)
    {
        int cell = Math.Clamp((int)value, 0, lines.Length - 2);
        return lines[cell] + (lines[cell + 1] - lines[cell]) * (value - cell);
    }
    private static double GridCoordinate(double[] lines, double pixel)
    {
        int cell = Array.FindIndex(lines, value => value > pixel) - 1;
        return cell + (pixel - lines[cell]) / (lines[cell + 1] - lines[cell]);
    }
    private static readonly BoardPoint[] Track =
    [
        new(1.5,6.5),new(2.5,6.5),new(3.5,6.5),new(4.5,6.5),new(5.5,6.5),
        new(6.5,5.5),new(6.5,4.5),new(6.5,3.5),new(6.5,2.5),new(6.5,1.5),new(6.5,0.5),new(7.5,0.5),new(8.5,0.5),
        new(8.5,1.5),new(8.5,2.5),new(8.5,3.5),new(8.5,4.5),new(8.5,5.5),
        new(9.5,6.5),new(10.5,6.5),new(11.5,6.5),new(12.5,6.5),new(13.5,6.5),new(14.5,6.5),new(14.5,7.5),new(14.5,8.5),
        new(13.5,8.5),new(12.5,8.5),new(11.5,8.5),new(10.5,8.5),new(9.5,8.5),
        new(8.5,9.5),new(8.5,10.5),new(8.5,11.5),new(8.5,12.5),new(8.5,13.5),new(8.5,14.5),new(7.5,14.5),new(6.5,14.5),
        new(6.5,13.5),new(6.5,12.5),new(6.5,11.5),new(6.5,10.5),new(6.5,9.5),
        new(5.5,8.5),new(4.5,8.5),new(3.5,8.5),new(2.5,8.5),new(1.5,8.5),new(0.5,8.5),new(0.5,7.5),new(0.5,6.5)
    ];

    public static BoardPoint Location(int seat, int token, int progress)
    {
        if (progress == GameEngine.Base)
        {
            bool right = seat is 1 or 2, bottom = seat is 2 or 3;
            double x = right ? (token % 2 == 0 ? 1048 : 919) : (token % 2 == 0 ? 202 : 331);
            double y = bottom ? (token < 2 ? 1016 : 889) : (token < 2 ? 202 : 329);
            return new(GridCoordinate(Columns, x), GridCoordinate(Rows, y));
        }
        if (progress < GameEngine.HomeLaneStart) return Track[GameEngine.TrackIndex(seat, progress)];
        double lane = progress - GameEngine.HomeLaneStart + 1.5;
        if (progress == GameEngine.Home) lane = 6.65;
        return seat switch { 0 => new(lane,7.5), 1 => new(7.5,lane), 2 => new(15-lane,7.5), _ => new(7.5,15-lane) };
    }

    public static ImmutableArray<TokenVisual> Tokens(GameStateView state)
    {
        var raw = new List<(int Player,int Token,int Seat,int Progress,BoardPoint Point)>();
        for (int p = 0; p < state.Players.Length; p++)
            for (int t = 0; t < 4; t++)
                raw.Add((p,t,state.Players[p].Seat,state.Players[p].Tokens[t],Location(state.Players[p].Seat,t,state.Players[p].Tokens[t])));
        var result = ImmutableArray.CreateBuilder<TokenVisual>();
        foreach (var group in raw.GroupBy(t => t.Point))
        {
            int i = 0, count = group.Count();
            foreach (var token in group)
            {
                double angle = count == 1 ? 0 : i++ * Math.PI * 2 / count;
                double radius = count == 1 ? 0 : Math.Min(0.32, 0.14 + count * 0.025);
                var point = ToPercent(new(token.Point.X + Math.Cos(angle) * radius, token.Point.Y + Math.Sin(angle) * radius));
                string position = token.Progress == GameEngine.Base ? "in base" : token.Progress == GameEngine.Home ? "finished"
                    : token.Progress >= GameEngine.HomeLaneStart ? $"home lane {token.Progress - 50}" : $"track square {GameEngine.TrackIndex(token.Seat, token.Progress)}";
                result.Add(new(token.Player, token.Token, token.Seat, point.X, point.Y, $"{Colours[token.Seat]} token {token.Token + 1}, {position}"));
            }
        }
        return result.ToImmutable();
    }

    public static string Describe(GameStateView view)
    {
        var evt = view.LastEvent;
        string colour = Colours[view.Players[evt.PlayerIndex].Seat];
        return evt.Kind switch
        {
            EventKind.Started => "Ready to roll.",
            EventKind.Rolled => $"{colour} rolled {evt.Die}.",
            EventKind.NoLegalMove => $"{colour} rolled {evt.Die}: no legal move. Turn passes.",
            EventKind.ThirdSix => $"{colour}: third six discarded. Earlier moves stand.",
            EventKind.Disqualified => $"{colour} disqualified.",
            _ => $"{colour} · {evt.Die} · token {evt.TokenId + 1} " +
                 (evt.From == -1 ? "left base" : evt.To == GameEngine.Home ? "finished" : $"advanced to {evt.To}") +
                 (evt.Captures.IsEmpty ? "" : $" · captured {evt.Captures.Length}") +
                 (evt.Bonus == BonusReason.None ? "." : $" · bonus: {evt.Bonus}.")
        };
    }
}
