using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ludo.Engine;
using Ludo.Presentation;

namespace Ludo.Warzone;

public sealed class BoardView : FrameworkElement
{
    private readonly BitmapImage board;
    private readonly BitmapSource[] pieces;
    private readonly BitmapImage[] dice;
    private readonly DispatcherTimer pulse = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private GameStateView? state;
    private string[] names = [];
    private double phase, rollPhase;
    private bool rolling;
    public BoardView()
    {
        BitmapImage Load(string name) => new(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", name)));
        board = Load("board.png");
        pieces = BoardPresentation.Colours.Select(c => (BitmapSource)new CroppedBitmap(Load($"token-{c.ToLowerInvariant()}.png"), new Int32Rect(330, 50, 610, 1150))).ToArray();
        dice = Enumerable.Range(1, 6).Select(i => Load($"dice-{i}.png")).ToArray();
        pulse.Tick += (_, _) => { phase += .12; InvalidateVisual(); };
        Unloaded += (_, _) => pulse.Stop();
    }
    public void Update(GameStateView value, string[]? playerNames = null)
    {
        state = value; if (playerNames is not null) names = playerNames;
        if (value.Phase == GamePhase.AwaitingMove && SystemParameters.ClientAreaAnimation) pulse.Start(); else pulse.Stop();
        InvalidateVisual();
    }
    public async Task RollAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds <= 0 || !SystemParameters.ClientAreaAnimation) return;
        rolling = true;
        try { for (int frame = 0; frame < 12; frame++) { rollPhase = frame / 11.0; InvalidateVisual(); await Task.Delay(Math.Max(1, milliseconds / 12), cancellationToken); } }
        finally { rolling = false; InvalidateVisual(); }
    }
    private void Text(DrawingContext context, string text, double x, double y, double size, Brush brush, double maxWidth = 1000)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(1, maxWidth), MaxLineCount = 2, Trimming = TextTrimming.CharacterEllipsis };
        context.DrawText(formatted, new Point(x, y));
    }
    protected override Size MeasureOverride(Size available)
    {
        double height = double.IsFinite(available.Height) ? available.Height : 720;
        return new(Math.Min(available.Width, height), height);
    }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        double width = ActualWidth, boardSize = Math.Min(width, ActualHeight - 132), left = (width - boardSize) / 2, top = 66;
        if (boardSize <= 0) return;
        context.DrawImage(board, new Rect(left, top, boardSize, boardSize));
        if (state is null) return;
        var legal = GameEngine.GetLegalMoves(state);
        foreach (var token in BoardPresentation.Tokens(state))
        {
            double x = left + token.X / 100 * boardSize, y = top + token.Y / 100 * boardSize, size = boardSize * .042;
            bool ready = token.Player == state.CurrentPlayer && legal.Contains(new Move(token.Token));
            double bounce = ready && SystemParameters.ClientAreaAnimation ? Math.Sin(phase * (1 + token.Token * .12) + token.Token) : 0;
            if (ready)
            {
                var colour = (Color)ColorConverter.ConvertFromString(BoardPresentation.HexColours[token.Seat]);
                context.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, colour.R, colour.G, colour.B)), null, new Point(x, y), size * .65, size * .55);
                context.DrawEllipse(new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), new Pen(Brushes.White, 2), new Point(x, y), size * .4, size * .3);
            }
            y -= (bounce + 1) * (ready ? 2 : 0);
            double pawnWidth = size * (1 - bounce * .035), pawnHeight = size * 1150 / 610 * (1 + bounce * .055);
            context.DrawImage(pieces[token.Seat], new Rect(x - pawnWidth / 2, y - pawnHeight * .9, pawnWidth, pawnHeight));
        }
        for (int p = 0; p < state.Players.Length; p++)
        {
            var player = state.Players[p]; bool active = state.Phase != GamePhase.Finished && p == state.CurrentPlayer;
            bool right = player.Seat is 1 or 2, bottom = player.Seat is 2 or 3;
            double x = right ? width / 2 + 6 : 2, y = bottom ? top + boardSize + 6 : 0, slot = width / 2 - 10;
            var colour = new SolidColorBrush((Color)ColorConverter.ConvertFromString(BoardPresentation.HexColours[player.Seat]));
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(active ? (byte)130 : (byte)65, 34, 71, 126)), active ? new Pen(colour, 2) : null, new Rect(x, y, slot, 60), 13, 13);
            context.DrawRoundedRectangle(new LinearGradientBrush(Colors.White, Color.FromRgb(178, 208, 239), 90), new Pen(colour, 3), new Rect(x + 6, y + 6, 47, 47), 10, 10);
            var ink = new SolidColorBrush(Color.FromRgb(40, 73, 121));
            context.DrawRoundedRectangle(ink, null, new Rect(x + 15, y + 20, 29, 24), 5, 5);
            context.DrawLine(new Pen(ink, 3), new Point(x + 30, y + 13), new Point(x + 30, y + 20));
            context.DrawEllipse(Brushes.LightCyan, null, new Point(x + 23, y + 29), 3, 3); context.DrawEllipse(Brushes.LightCyan, null, new Point(x + 37, y + 29), 3, 3);
            Text(context, names.ElementAtOrDefault(p) ?? BoardPresentation.Colours[player.Seat], x + 62, y + 9, 12, Brushes.White, slot - (active ? 122 : 67));
            Text(context, $"{BoardPresentation.Colours[player.Seat]} · {player.Tokens.Count(t => t == 56)}/4 home", x + 62, y + 38, 10, Brushes.LightSteelBlue, slot - (active ? 122 : 67));
            if (active)
            {
                double dx = x + slot - 53, dy = y + 7;
                context.DrawRoundedRectangle(new LinearGradientBrush(Colors.LightYellow, Colors.Goldenrod, 90), new Pen(Brushes.LightYellow, 2), new Rect(dx, dy, 46, 46), 9, 9);
                if (rolling) context.PushTransform(new RotateTransform(rollPhase * 360, dx + 23, dy + 23));
                int face = rolling ? (int)(rollPhase * 11) % 6 : Math.Clamp(state.LastEvent.PlayerIndex == p ? state.LastEvent.Die - 1 : 0, 0, 5);
                context.DrawImage(dice[face], new Rect(dx + 3, dy + 3 - (rolling ? Math.Sin(rollPhase * Math.PI) * 7 : 0), 40, 40));
                if (rolling) context.Pop();
            }
        }
    }
}
