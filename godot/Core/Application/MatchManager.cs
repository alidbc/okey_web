using System;
using System.Collections.Generic;
using System.Linq;
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
    public bool IsPairWin { get; set; }
    public bool IsOkeyFinish { get; set; }

    // In a real Okey game, each player drops to the player on their right.
    // For simplicity mirroring the React version, we just store what they discarded
    public Dictionary<string, List<Tile>> PlayerDiscardPiles { get; set; }

    public event Action OnGameStateChanged;
    public event Action<string, Tile, bool, int, bool> OnTileDrawn; // pid, tile, fromDiscard, targetIndex, isDrag
    public event Action<string, Tile, int> OnTileDiscarded; // pid, tile, rackIndex
    public event Action<string, Tile> OnIndicatorShown; // pid, indicatorTile
    public event Action OnAutoMoveExecuted;

    public MatchManager()
    {
        Status = GameStatus.Playing; // Or some initial game state
        Players = new List<Player>();
        PlayerDiscardPiles = new Dictionary<string, List<Tile>>();
        DiscardPile = new List<Tile>();
    }

    public void AddPlayer(Player player)
    {
        if (!Players.Contains(player))
        {
            Players.Add(player);
        }
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

    public int DrawFromDeck(string playerId, int targetIndex = -1, bool isDrag = false)
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
        
        OnTileDrawn?.Invoke(playerId, drawn, false, finalIndex, isDrag);
        OnGameStateChanged?.Invoke();
        PersistenceManager.SaveMatch(playerId, this); // Simple ID for now
        return finalIndex;
    }

    public int DrawFromDiscard(string playerId, int targetIndex = -1, bool isDrag = false)
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
        
        OnTileDrawn?.Invoke(playerId, drawn, true, finalIndex, isDrag);
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

        // Or if it's the center discard based on the UI
        PlayerDiscardPiles[playerId].Add(tileToDiscard);
        
        OnTileDiscarded?.Invoke(playerId, tileToDiscard, tileIndex);
        PersistenceManager.SaveMatch(playerId, this);
        NextTurn();
        return true;
    }

    public bool CanShowIndicator(string playerId)
    {
        int pIdx = Players.FindIndex(p => p.Id == playerId);
        if (pIdx == -1 || pIdx != CurrentPlayerIndex) return false;

        // Player 0 starts in Discard phase on Turn 1
        if (pIdx == 0)
        {
            if (TurnID != 1 || CurrentPhase != TurnPhase.Discard) return false;
        }
        else
        {
            // Other players show it during their first Draw phase
            if (TurnID != pIdx + 1 || CurrentPhase != TurnPhase.Draw) return false;
        }

        var player = Players[pIdx];
        return player.Rack.Any(t => t != null && t.Value == IndicatorTile.Value && t.Color == IndicatorTile.Color && !t.IsFakeOkey);
    }

    public bool ShowIndicator(string playerId)
    {
        if (!CanShowIndicator(playerId)) return false;

        // Deduction for opponents
        foreach (var p in Players)
        {
            if (p.Id != playerId)
            {
                p.IndicatorPenaltyApplied = true; 
            }
        }

        OnIndicatorShown?.Invoke(playerId, IndicatorTile);
        OnGameStateChanged?.Invoke();
        return true;
    }

    public void ForceWin(string playerId)
    {
        var player = Players.Find(p => p.Id == playerId);
        if (player == null) return;

        Status = GameStatus.Victory;
        WinnerId = playerId;
        WinnerTiles = player.Rack.ToList();
        
        // Scores are computed dynamically in GetPlayerScores based on Status and WinnerId
        OnGameStateChanged?.Invoke();
    }

    public (bool success, string message) FinishGame(string playerId, int finishTileIndex)
    {
        if (Status != GameStatus.Playing || CurrentPhase != TurnPhase.Discard || Players[CurrentPlayerIndex].Id != playerId)
            return (false, "Not your turn to finish.");

        var player = Players[CurrentPlayerIndex];
        Tile finishTile = player.Rack[finishTileIndex];

        // Handle finish by preserving gaps so ValidateHandGroups/Pairs can identify clusters
        var validationList = new List<Tile>();
        for (int i = 0; i < player.Rack.Length; i++)
        {
            if (i == finishTileIndex)
                validationList.Add(null);
            else
                validationList.Add(player.Rack[i]);
        }
        
        var (isValidSets, setsReason) = RuleEngine.ValidateHandGroups(validationList);
        var (isValidPairs, pairsReason) = RuleEngine.ValidatePairs(validationList);

        if (isValidSets || isValidPairs)
        {
            Status = GameStatus.Victory;
            WinnerId = playerId;
            IsPairWin = isValidPairs;
            IsOkeyFinish = finishTile != null && finishTile.IsWildcard;
            
            // Store the entire rack to preserve organization/gaps
            WinnerTiles = new List<Tile>(player.Rack);
            if (finishTileIndex >= 0 && finishTileIndex < WinnerTiles.Count)
            {
                WinnerTiles[finishTileIndex] = null;
            }

            OnGameStateChanged?.Invoke();
            return (true, "Victory!");
        }

        return (false, isValidPairs ? string.Empty : $"{setsReason} / {pairsReason}");
    }

    public List<PlayerScore> GetPlayerScores()
    {
        var scores = new List<PlayerScore>();
        foreach (var p in Players)
        {
            bool isWinner = Status == GameStatus.Victory && p.Id == WinnerId;
            
            // Standard Okey point-countdown system
            // Players start at 20 (or any configured value).
            // Winner's score doesn't change, others lose points.
            
            int startingPoints = 20; 
            int deduction = 0;

            if (Status == GameStatus.Victory && !isWinner)
            {
                if (IsPairWin) deduction = 4;
                else if (IsOkeyFinish) deduction = 4;
                else deduction = 2; // Normal sets/runs win
            }

            if (p.IndicatorPenaltyApplied)
            {
                deduction += 1;
            }

            int finalScore = Math.Max(0, startingPoints - deduction);

            scores.Add(new PlayerScore
            {
                PlayerId = p.Id,
                PlayerName = p.Name,
                AvatarUrl = p.AvatarUrl,
                IsWinner = isWinner,
                Score = finalScore
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
        if (!player.IsBot && player.ConsecutiveMissedTurns >= 1)
        {
            GD.Print($"MatchManager: Player {player.Id} missed {player.ConsecutiveMissedTurns} turns. Replacing with bot.");
            
            // Upgrade to BotPlayer
            var bot = new BotPlayer(player.Id, player.Name + " (Bot)", player.AvatarUrl, this);
            bot.Rack = player.Rack;
            bot.SeatIndex = player.SeatIndex;
            bot.IsActive = player.IsActive;
            bot.ConsecutiveMissedTurns = player.ConsecutiveMissedTurns;
            bot.ConnectionState = PlayerConnectionState.REPLACED_BY_BOT;
            
            Players[CurrentPlayerIndex] = bot;
            
            // Force a state sync so clients see the bot indicator immediately
            OnGameStateChanged?.Invoke();
        }
    }

    public void CheckTimeouts()
    {
        if (Status != GameStatus.Playing) return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Player p = Players[CurrentPlayerIndex];
        int currentTurnDuration = p.IsBot ? 2 : TurnDuration;

        if (now - TurnStartTimestamp > currentTurnDuration)
        {
            ExecuteAutoMove();
            CheckBotReplacement();
        }
    }

    public void ExecuteAutoMove()
    {
        Player p = Players[CurrentPlayerIndex];
        
        if (CurrentPhase == TurnPhase.Draw)
        {
            DrawFromDeck(p.Id);
            
            // If it's a bot, completes the turn by discarding immediately as well
            if (p.IsBot)
            {
                AutoDiscard(p);
            }
        }
        else if (CurrentPhase == TurnPhase.Discard)
        {
            AutoDiscard(p);
            p.ConsecutiveMissedTurns++;
        }
        
        OnAutoMoveExecuted?.Invoke();
    }

    private void AutoDiscard(Player p)
    {
        int tileIndex = -1;
        if (p is BotPlayer bot)
        {
            tileIndex = bot.EvaluateLeastValuableTile();
        }
        else
        {
            // Fallback for non-bot players (e.g. human timeout)
            for (int i = RackSize - 1; i >= 0; i--)
            {
                if (p.Rack[i] != null)
                {
                    tileIndex = i;
                    break;
                }
            }
        }
        
        if (tileIndex != -1)
        {
            DiscardTile(p.Id, tileIndex);
        }
    }

    public void ResetConsecutiveMissedTurns(string playerId)
    {
        var player = Players.Find(p => p.Id == playerId);
        if (player != null)
        {
            player.ConsecutiveMissedTurns = 0;
        }
    }

    private const int RackSize = 26;
}
