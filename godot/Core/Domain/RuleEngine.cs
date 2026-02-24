using System.Collections.Generic;
using System.Linq;

namespace OkieRummyGodot.Core.Domain;

public static class RuleEngine
{
    public static (bool isValid, string reason) ValidateHandGroups(List<Tile> rack)
    {
        var clusters = new List<List<Tile>>();
        var currentCluster = new List<Tile>();
        
        foreach (var tile in rack)
        {
            if (tile != null)
            {
                currentCluster.Add(tile);
            }
            else if (currentCluster.Count > 0)
            {
                clusters.Add(new List<Tile>(currentCluster));
                currentCluster.Clear();
            }
        }
        
        if (currentCluster.Count > 0)
        {
            clusters.Add(new List<Tile>(currentCluster));
        }
        
        int totalTiles = clusters.Sum(c => c.Count);
        if (totalTiles != 14)
        {
            return (false, $"You need exactly 14 tiles to finish (currently used: {totalTiles}).");
        }
        
        foreach (var cluster in clusters)
        {
            if (cluster.Count < 3) return (false, "All groups must have at least 3 tiles.");
            if (!IsRun(cluster) && !IsSet(cluster)) return (false, "Found a group that is neither a valid Set nor a Run.");
        }
        
        return (true, string.Empty);
    }

    private static bool IsSet(List<Tile> tiles)
    {
        if (tiles.Count > 4) return false;
        
        var nonWild = tiles.Where(t => !t.IsWildcard).ToList();
        if (nonWild.Count == 0) return true;
        
        int baseValue = nonWild[0].Value;
        if (nonWild.Any(t => t.Value != baseValue)) return false;
        
        var colors = new HashSet<TileColor>(nonWild.Select(t => t.Color));
        if (colors.Count != nonWild.Count) return false;
        
        return true;
    }

    private static bool IsRun(List<Tile> tiles)
    {
        var nonWild = tiles.Where(t => !t.IsWildcard).ToList();
        if (nonWild.Count == 0) return true;
        
        TileColor baseColor = nonWild[0].Color;
        if (nonWild.Any(t => t.Color != baseColor)) return false;

        bool CheckSequence(bool treatOneAsFourteen)
        {
            int firstIdx = tiles.FindIndex(t => !t.IsWildcard);
            int firstVal = tiles[firstIdx].Value;
            if (treatOneAsFourteen && firstVal == 1) firstVal = 14;
            
            for (int i = firstIdx + 1; i < tiles.Count; i++)
            {
                if (!tiles[i].IsWildcard)
                {
                    int currentVal = tiles[i].Value;
                    if (treatOneAsFourteen && currentVal == 1) currentVal = 14;
                    int diff = i - firstIdx;
                    if (currentVal != firstVal + diff) return false;
                }
            }
            
            int startVal = firstVal - firstIdx;
            int endVal = startVal + tiles.Count - 1;
            
            if (treatOneAsFourteen)
            {
                if (startVal < 1 || endVal > 14) return false;
            }
            else
            {
                if (startVal < 1 || endVal > 13) return false;
            }
            
            return true;
        }

        if (CheckSequence(false)) return true;
        
        bool hasOne = nonWild.Any(t => t.Value == 1);
        if (hasOne && CheckSequence(true)) return true;
        return false;
    }

    public static int CalculatePenalty(Tile[] rack)
    {
        int penalty = 0;
        foreach (var tile in rack)
        {
            if (tile == null) continue;
            
            if (tile.IsWildcard)
            {
                penalty += 20; // High penalty for wildcards
            }
            else
            {
                penalty += tile.Value;
            }
        }
        return penalty;
    }
}
