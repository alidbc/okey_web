using System;
using System.Collections.Generic;
using Godot;
using OkieRummyGodot.Core.Domain;

namespace OkieRummyGodot.Core.Application;

public class MatchManager
{
    public GameStatus Status { get; set; }
    public TurnPhase CurrentPhase { get; set; }
    
    public Deck GameDeck { get; set; }
    public Tile IndicatorTile { get; set; }
    public Tile OkeyTile { get; set; }
    
    public List<Player> Players { get; set; }
    public int CurrentPlayerIndex { get; set; }
    
    public long TurnID { get; set; }
    public long TurnStartTimestamp { get; set; }
    public int TurnDuration { get; set; } = 30; // Default 30 seconds
    public GamePhase Phase { get; set; }
    
    public List<Tile> DiscardPile { get; set; } // Center discard pile
    
    public string WinnerId { get; set; }
    public List<Tile> WinnerTiles { get; set; }

    // In a real Okey game, each player drops to the player on their right.
    // For simplicity mirroring the React version, we just store what they discarded
    public Dictionary<string, List<Tile>> PlayerDiscardPiles { get; set; }

    public event Action OnGameStateChanged;
    public event Action<string, Tile, bool> OnTileDrawn;
    public event Action<string, Tile> OnTileDiscarded;

    public MatchManager()
    {
        Status = GameStatus.Menu;
        Players = new List<Player>();
        PlayerDiscardPiles = new Dictionary<string, List<Tile>>();
        DiscardPile = new List<Tile>();
    }

    public void AddPlayer(Player player)
    {
        Players.Add(player);
        PlayerDiscardPiles[player.Id] = new List<Tile>();
    }

    public void StartGame()
    {
        if (Players.Count < 2 || Players.Count > 4) throw new InvalidOperationException("Need between 2 and 4 players.");
        
        GameDeck = new Deck();
        GameDeck.Shuffle();
        
        IndicatorTile = GameDeck.DrawIndicator();
        DetermineOkeyTile();
        
        DiscardPile.Clear();
        foreach (var p in Players)
        {
            PlayerDiscardPiles[p.Id] = new List<Tile>();
            // Clear rack
            for(int i=0; i < 26; i++) p.Rack[i] = null;
        }

        // Deal tiles (15 to first player, 14 to others)
        for (int i = 0; i < 15; i++) Players[0].AddTileToFirstEmptySlot(GameDeck.Draw());
        for (int pIdx = 1; pIdx < Players.Count; pIdx++)
        {
            for (int i = 0; i < 14; i++) Players[pIdx].AddTileToFirstEmptySlot(GameDeck.Draw());
        }

        CurrentPlayerIndex = 0;
        Players[0].IsActive = true;
        
        CurrentPhase = TurnPhase.Discard;
        Status = GameStatus.Playing;
        Phase = GamePhase.Playing;
        
        TurnID = 1;
        TurnStartTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        OnGameStateChanged?.Invoke();
    }

    private void DetermineOkeyTile()
    {
        int okeyValue = IndicatorTile.Value == 13 ? 1 : IndicatorTile.Value + 1;
        OkeyTile = new Tile("okey_ref", okeyValue, IndicatorTile.Color);
        
        // Apply wildcard status and fix Fake Okeys in the deck
        GameDeck.ApplyOkeyRules(okeyValue, IndicatorTile.Color);
    }

    public Tile NextDeckTileHint { get; set; }

    public Tile PeekDeck() 
    {
        if (NextDeckTileHint != null) return NextDeckTileHint;
        return GameDeck?.Peek();
    }

    public int DrawFromDeck(string playerId, int targetIndex = -1)
    {
        if (Status != GameStatus.Playing || CurrentPhase != TurnPhase.Draw || Players[CurrentPlayerIndex].Id != playerId)
            return -1;

        Tile drawn = GameDeck.Draw();
        if (drawn == null) return -1;

        Player p = Players[CurrentPlayerIndex];
        int finalIndex = -1;
        if (targetIndex != -1)
        {
            finalIndex = p.AddTileToSlot(drawn, targetIndex);
        }
        
        if (finalIndex == -1)
        {
            finalIndex = p.AddTileToFirstEmptySlot(drawn);
        }

        if (finalIndex == -1) return -1; // Should not happen in normal Okey

        CurrentPhase = TurnPhase.Discard;
        
        OnTileDrawn?.Invoke(playerId, drawn, false);
        OnGameStateChanged?.Invoke();
        PersistenceManager.SaveMatch(playerId, this); // Simple ID for now
        return finalIndex;
    }

    public int DrawFromDiscard(string playerId, int targetIndex = -1)
    {
        if (Status != GameStatus.Playing || CurrentPhase != TurnPhase.Draw || Players[CurrentPlayerIndex].Id != playerId)
            return -1;
            
        // In Okey, you draw from the player to your left (the previous player in turn order)
        int leftPlayerIndex = (CurrentPlayerIndex - 1 + Players.Count) % Players.Count;
        string leftPlayerId = Players[leftPlayerIndex].Id;
        
        var leftPile = PlayerDiscardPiles[leftPlayerId];
        if (leftPile.Count == 0) return -1;

        Tile drawn = leftPile[^1];
        leftPile.RemoveAt(leftPile.Count - 1);
        
        Player p = Players[CurrentPlayerIndex];
        int finalIndex = -1;
        if (targetIndex != -1)
        {
            finalIndex = p.AddTileToSlot(drawn, targetIndex);
        }
        
        if (finalIndex == -1)
        {
            finalIndex = p.AddTileToFirstEmptySlot(drawn);
        }

        if (finalIndex == -1) return -1;

        CurrentPhase = TurnPhase.Discard;
        
        OnTileDrawn?.Invoke(playerId, drawn, true);
        OnGameStateChanged?.Invoke();
        PersistenceManager.SaveMatch(playerId, this);
        return finalIndex;
    }

    public bool DiscardTile(string playerId, int tileIndex)
    {
        if (Status != GameStatus.Playing || CurrentPhase != TurnPhase.Discard || Players[CurrentPlayerIndex].Id != playerId)
            return false;

        Tile tileToDiscard = Players[CurrentPlayerIndex].RemoveTile(tileIndex);
        if (tileToDiscard == null) return false;

        // Add to this player's discard pile (which is the draw pile for the next player)
        // Or if it's the center discard based on the UI
        PlayerDiscardPiles[playerId].Add(tileToDiscard);
        
        OnTileDiscarded?.Invoke(playerId, tileToDiscard);
        PersistenceManager.SaveMatch(playerId, this);
        NextTurn();
        return true;
    }

    public (bool success, string message) FinishGame(string playerId, int finishTileIndex)
    {
        if (Status != GameStatus.Playing || CurrentPhase != TurnPhase.Discard || Players[CurrentPlayerIndex].Id != playerId)
            return (false, "Not your turn to finish.");

        var player = Players[CurrentPlayerIndex];
        
        // Handle finish by preserving gaps so ValidateHandGroups can identify clusters
        var validationList = new List<Tile>();
        for (int i = 0; i < player.Rack.Length; i++)
        {
            // Treat the finishing tile as a gap (null) for validation
            if (i == finishTileIndex)
                validationList.Add(null);
            else
                validationList.Add(player.Rack[i]);
        }
        
        var (isValid, reason) = RuleEngine.ValidateHandGroups(validationList);
        if (isValid)
        {
            Status = GameStatus.Victory;
            WinnerId = playerId;
            
            // Store the entire rack to preserve organization/gaps
            WinnerTiles = new List<Tile>(player.Rack);
            // Specifically null out the finishing tile if it's still there
            if (finishTileIndex >= 0 && finishTileIndex < WinnerTiles.Count)
            {
                WinnerTiles[finishTileIndex] = null;
            }

            OnGameStateChanged?.Invoke();
            return (true, "Victory!");
        }

        return (false, reason);
    }

    public List<PlayerScore> GetPlayerScores()
    {
        var scores = new List<PlayerScore>();
        foreach (var p in Players)
        {
            bool isWinner = Status == GameStatus.Victory && p.Id == WinnerId;
            int scoreValue = 0;

            if (isWinner)
            {
                scoreValue = 100; // Bonus for winning
            }
            else
            {
                // Penalty-based scoring: 100 - total penalty (capped at 0)
                int penalty = RuleEngine.CalculatePenalty(p.Rack);
                scoreValue = Math.Max(0, 100 - penalty);
            }

            scores.Add(new PlayerScore
            {
                PlayerId = p.Id,
                PlayerName = p.Name,
                AvatarUrl = p.AvatarUrl,
                IsWinner = isWinner,
                Score = scoreValue
            });
        }
        return scores;
    }

    private void NextTurn()
    {
        if (GameDeck.RemainingCount == 0)
        {
            Status = GameStatus.GameOver;
            OnGameStateChanged?.Invoke();
            return;
        }

        Players[CurrentPlayerIndex].IsActive = false;
        CurrentPlayerIndex = (CurrentPlayerIndex + 1) % Players.Count;
        Players[CurrentPlayerIndex].IsActive = true;
        
        CurrentPhase = TurnPhase.Draw;
        TurnID++;
        TurnStartTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        // Bot replacement trigger
        CheckBotReplacement();
        
        OnGameStateChanged?.Invoke();
    }

    private void CheckBotReplacement()
    {
        var player = Players[CurrentPlayerIndex];
        if (!player.IsBot && player.ConsecutiveMissedTurns >= 3)
        {
            GD.Print($"MatchManager: Player {player.Id} missed {player.ConsecutiveMissedTurns} turns. Replacing with bot.");
            player.IsBot = true;
            player.ConnectionState = PlayerConnectionState.REPLACED_BY_BOT;
        }
    }

    public void CheckTimeouts()
    {
        if (Status != GameStatus.Playing) return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now - TurnStartTimestamp > TurnDuration)
        {
            ExecuteAutoMove();
        }
    }

    public void ExecuteAutoMove()
    {
        Player p = Players[CurrentPlayerIndex];
        
        if (CurrentPhase == TurnPhase.Draw)
        {
            // Auto draw from deck
            DrawFromDeck(p.Id);
        }
        else if (CurrentPhase == TurnPhase.Discard)
        {
            // Auto discard: random tile or last drawn
            // Simple strategy: discard last occupied slot or first available tile
            int lastIndex = -1;
            for (int i = RackSize - 1; i >= 0; i--)
            {
                if (p.Rack[i] != null)
                {
                    lastIndex = i;
                    break;
                }
            }
            
            if (lastIndex != -1)
            {
                DiscardTile(p.Id, lastIndex);
                p.ConsecutiveMissedTurns++;
            }
        }
    }

    private const int RackSize = 26;
}
