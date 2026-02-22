using System;
using System.Collections.Generic;

namespace OkieRummyGodot.Core.Domain;

public class Deck
{
    private readonly List<Tile> _tiles;
    private static readonly Random Rng = new Random();

    public Deck()
    {
        _tiles = new List<Tile>();
        Initialize();
    }

    private void Initialize()
    {
        int idCounter = 1;
        // Two sets of 1-13 in 4 colors
        for (int set = 0; set < 2; set++)
        {
            foreach (TileColor color in Enum.GetValues(typeof(TileColor)))
            {
                for (int value = 1; value <= 13; value++)
                {
                    _tiles.Add(new Tile($"tile_{idCounter++}", value, color));
                }
            }
        }
        
        // Add 2 Fake Okeys
        _tiles.Add(new Tile($"fake_okey_{idCounter++}", 0, TileColor.Black, isFakeOkey: true));
        _tiles.Add(new Tile($"fake_okey_{idCounter++}", 0, TileColor.Red, isFakeOkey: true));
    }

    public void Shuffle()
    {
        int n = _tiles.Count;
        while (n > 1)
        {
            n--;
            int k = Rng.Next(n + 1);
            Tile value = _tiles[k];
            _tiles[k] = _tiles[n];
            _tiles[n] = value;
        }
    }

    public Tile Draw()
    {
        if (_tiles.Count == 0) return null;
        
        Tile tile = _tiles[_tiles.Count - 1];
        _tiles.RemoveAt(_tiles.Count - 1);
        return tile;
    }
    
    public Tile DrawIndicator() => Draw(); // Draws a random tile to be the indicator

    public Tile Peek()
    {
        if (_tiles.Count == 0) return null;
        return _tiles[_tiles.Count - 1];
    }

    public int RemainingCount => _tiles.Count;
}
