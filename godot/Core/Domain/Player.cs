using System.Collections.Generic;

namespace OkieRummyGodot.Core.Domain;

public class Player
{
    public string Id { get; }
    public string Name { get; }
    public string AvatarUrl { get; }
    public bool IsActive { get; set; }
    public bool IsBot { get; }
    
    // The player's tile rack (fixed size of 26 in Okey)
    public Tile[] Rack { get; }
    
    public Player(string id, string name, string avatarUrl, bool isBot = false)
    {
        Id = id;
        Name = name;
        AvatarUrl = avatarUrl;
        IsBot = isBot;
        IsActive = false;
        Rack = new Tile[26];
    }

    public int AddTileToFirstEmptySlot(Tile tile)
    {
        for (int i = 0; i < Rack.Length; i++)
        {
            if (Rack[i] == null)
            {
                Rack[i] = tile;
                return i;
            }
        }
        return -1;
    }

    public int AddTileToSlot(Tile tile, int index)
    {
        if (index < 0 || index >= Rack.Length) return -1;
        if (Rack[index] == null)
        {
            Rack[index] = tile;
            return index;
        }
        return -1;
    }

    public bool MoveTile(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Rack.Length || toIndex < 0 || toIndex >= Rack.Length)
            return false;

        Tile temp = Rack[fromIndex];
        Rack[fromIndex] = Rack[toIndex];
        Rack[toIndex] = temp;
        return true;
    }

    public Tile RemoveTile(int index)
    {
        if (index < 0 || index >= Rack.Length || Rack[index] == null)
            return null;

        Tile tile = Rack[index];
        Rack[index] = null;
        return tile;
    }

    public List<Tile> GetValidTiles()
    {
        var validTiles = new List<Tile>();
        foreach (var tile in Rack)
        {
            if (tile != null) validTiles.Add(tile);
        }
        return validTiles;
    }

    public void QuickSortRack()
    {
        var validTiles = GetValidTiles();
        
        validTiles.Sort((a, b) =>
        {
            if (a.IsWildcard && !b.IsWildcard) return -1;
            if (!a.IsWildcard && b.IsWildcard) return 1;
            if (a.Color != b.Color) return a.Color.CompareTo(b.Color);
            return a.Value.CompareTo(b.Value);
        });

        for (int i = 0; i < Rack.Length; i++)
        {
            Rack[i] = i < validTiles.Count ? validTiles[i] : null;
        }
    }
}
