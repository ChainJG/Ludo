using System.Collections.Immutable;
using Ludo.Engine;

namespace Ludo.Bots.Versions;

public sealed class NeuralV7 : IBot
{
    private readonly NeuralModel model;
    public NeuralV7(NeuralModel model) { model.Validate(); this.model = model; }
    public string Id => "neural";
    public int Version => 7;
    public string Label => "Neural v7";
    public BotChoice Analyze(GameStateView view, ImmutableArray<Move> legalMoves, IRng rng)
    {
        if (legalMoves.IsEmpty) throw new ArgumentException("A bot requires a legal move.");
        var candidates = legalMoves.Select(move => new MoveScore(move, NeuralNetwork.Score(model, NeuralNetwork.Features(view, move)),
            $"Neural preference · {model.Name}")).OrderByDescending(c => c.Score).ThenBy(c => c.Move.TokenId).ToImmutableArray();
        return new(candidates[0].Move, candidates, legalMoves.Length);
    }
}
