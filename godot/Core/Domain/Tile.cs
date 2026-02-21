using System;

namespace OkieRummyGodot.Core.Domain;

/// <summary>
/// Represents a single tile in the game.
/// Contains no UI knowledge or Unity/Godot dependencies.
/// </summary>
public class Tile
{
    public string Id { get; }
    public int Value { get; }
    public TileColor Color { get; }
    public bool IsFakeOkey { get; }
    public bool IsWildcard { get; set; }

    public Tile(string id, int value, TileColor color, bool isFakeOkey = false)
    {
        Id = id;
        Value = value;
        Color = color;
        IsFakeOkey = isFakeOkey;
        IsWildcard = isFakeOkey; // Fake Okey is always a wildcard initially
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
