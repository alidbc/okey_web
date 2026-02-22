using System;
using System.Collections.Generic;
using OkieRummyGodot.Core.Domain;

namespace OkieRummyGodot.Core.Application;

public class MatchManager
{
    public GameStatus Status { get; private set; }
    public TurnPhase CurrentPhase { get; private set; }
    
    public Deck GameDeck { get; private set; }
    public Tile IndicatorTile { get; private set; }
    public Tile OkeyTile { get; private set; }
    
    public List<Player> Players { get; private set; }
    public int CurrentPlayerIndex { get; private set; }
    
    public List<Tile> DiscardPile { get; private set; } // Center discard pile
    
    // In a real Okey game, each player drops to the player on their right.
    // For simplicity mirroring the React version, we just store what they discarded
    public Dictionary<string, List<Tile>> PlayerDiscardPiles { get; private set; }

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
        if (Players.Count != 4) throw new InvalidOperationException("Need exactly 4 players.");
        
        GameDeck = new Deck();
        GameDeck.Shuffle();
        
        IndicatorTile = GameDeck.DrawIndicator();
        DetermineOkeyTile();
        
        DiscardPile.Clear();
        foreach (var p in Players)
        {
            PlayerDiscardPiles[p.Id].Clear();
            // Clear rack
            for(int i=0; i < 26; i++) p.Rack[i] = null;
        }

        // Deal tiles (15 to first player, 14 to others)
        for (int i = 0; i < 15; i++) Players[0].AddTileToFirstEmptySlot(GameDeck.Draw());
        for (int pIdx = 1; pIdx < 4; pIdx++)
        {
            for (int i = 0; i < 14; i++) Players[pIdx].AddTileToFirstEmptySlot(GameDeck.Draw());
        }

        CurrentPlayerIndex = 0;
        Players[0].IsActive = true;
        
        // First player starts with 15 tiles, so they skip draw phase and go straight to discard
        CurrentPhase = TurnPhase.Discard;
        Status = GameStatus.Playing;
        
        OnGameStateChanged?.Invoke();
    }

    private void DetermineOkeyTile()
    {
        int okeyValue = IndicatorTile.Value == 13 ? 1 : IndicatorTile.Value + 1;
        OkeyTile = new Tile("okey_ref", okeyValue, IndicatorTile.Color);
        
        // Apply wildcard status to all matching tiles in deck before drawing
        // This is simplified; normally evaluate upon drawing/viewing
    }

    public Tile PeekDeck() => GameDeck?.Peek();

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
        NextTurn();
        return true;
    }

    public (bool success, string message) FinishGame(string playerId)
    {
        if (Status != GameStatus.Playing || CurrentPhase != TurnPhase.Discard || Players[CurrentPlayerIndex].Id != playerId)
            return (false, "Not your turn to finish.");

        var player = Players[CurrentPlayerIndex];
        var validTiles = player.GetValidTiles(); // Should be 14 at this point (15 minus the 1 they want to discard)
        
        var (isValid, reason) = RuleEngine.ValidateHandGroups(validTiles);
        if (isValid)
        {
            Status = GameStatus.Victory;
            OnGameStateChanged?.Invoke();
            return (true, "Victory!");
        }

        return (false, reason);
    }

    private void NextTurn()
    {
        Players[CurrentPlayerIndex].IsActive = false;
        CurrentPlayerIndex = (CurrentPlayerIndex + 1) % Players.Count;
        Players[CurrentPlayerIndex].IsActive = true;
        
        CurrentPhase = TurnPhase.Draw;
        OnGameStateChanged?.Invoke();
    }
}
