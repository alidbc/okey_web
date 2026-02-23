using System;

namespace OkieRummyGodot.Core.Domain;

/// <summary>
/// Represents a single tile in the game.
/// Contains no UI knowledge or Unity/Godot dependencies.
/// </summary>
public class Tile
{
    public string Id { get; set; }
    public int Value { get; set; }
    public TileColor Color { get; set; }
    public bool IsFakeOkey { get; set; }
    public bool IsWildcard { get; set; }

    public Tile() { } // Required for serialization

    public Tile(string id, int value, TileColor color, bool isFakeOkey = false)
    {
        Id = id;
        Value = value;
        Color = color;
        IsFakeOkey = isFakeOkey;
        IsWildcard = false; 
    }
    
    // For cloning when dealing with hand evaluation
    public Tile Clone()
    {
        return new Tile(Id, Value, Color, IsFakeOkey)
        {
            IsWildcard = this.IsWildcard
        };
    }

    public override string ToString()
    {
        if (IsFakeOkey) return "[Fake Okey]";
        return $"[{Color} {Value}]" + (IsWildcard ? "*" : "");
    }
}
