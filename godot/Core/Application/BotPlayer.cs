using System;
using System.Threading.Tasks;
using OkieRummyGodot.Core.Domain;
using System.Collections.Generic;

namespace OkieRummyGodot.Core.Application;

public class BotPlayer : Player
{
    private readonly MatchManager _matchManager;
    private static readonly Random Rng = new Random();

    public BotPlayer(string id, string name, string avatarUrl, MatchManager matchManager) 
        : base(id, name, avatarUrl, isBot: true)
    {
        _matchManager = matchManager;
    }

    /// <summary>
    /// Called when it's the bot's turn to play.
    /// Simulates thinking time and executes a move.
    /// </summary>
    public async Task PlayTurnAsync()
    {
        if (!IsActive || _matchManager.CurrentPhase != TurnPhase.Draw) return;
        
        await Task.Delay(800); // Simulate thinking before drawing

        // Example logic from original simulateBotTurns
        // 30% chance to draw from discard pile if it has tiles
        bool hasDiscard = _matchManager.DiscardPile.Count > 0;
        bool drawFromDiscard = hasDiscard && Rng.NextDouble() < 0.3;

        if (drawFromDiscard)
        {
            _matchManager.DrawFromDiscard(Id);
        }
        else
        {
            _matchManager.DrawFromDeck(Id);
        }

        await Task.Delay(400); // Simulate thinking before discarding

        if (_matchManager.CurrentPhase == TurnPhase.Discard)
        {
            // Simple logic: discard the first non-null tile or evaluate least valuable tile
            int tileToDiscardIndex = EvaluateLeastValuableTile();
            _matchManager.DiscardTile(Id, tileToDiscardIndex);
        }
    }

    private int EvaluateLeastValuableTile()
    {
        // For standard Okey bot, finding the tile least likely to complete a set/run
        // As a placeholder, we pick a random non-wildcard tile
        var validIndices = new List<int>();
        for (int i = 0; i < Rack.Length; i++)
        {
            if (Rack[i] != null && !Rack[i].IsWildcard)
            {
                validIndices.Add(i);
            }
        }
        
        // If all are wildcards (extremely rare), pick any non-null
        if (validIndices.Count == 0)
        {
            for (int i = 0; i < Rack.Length; i++)
            {
                if (Rack[i] != null) validIndices.Add(i);
            }
        }

        if (validIndices.Count == 0) return -1;
        return validIndices[Rng.Next(validIndices.Count)];
    }
}
