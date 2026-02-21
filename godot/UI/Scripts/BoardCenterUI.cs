using Godot;
using OkieRummyGodot.Core.Domain;
using System;

namespace OkieRummyGodot.UI.Scripts;

public partial class BoardCenterUI : HBoxContainer
{
    [Export] public DeckUI DeckCountBadge;
    [Export] public Control IndicatorContainer;
    
    private TileUI _indicatorTile;

    public override void _Ready()
    {
        if (IndicatorContainer != null)
        {
            _indicatorTile = IndicatorContainer.GetNodeOrNull<TileUI>("TileUI");
            
            // Dim and border the indicator tile like React styling
            if (_indicatorTile != null)
            {
                _indicatorTile.BaseModulate = new Color(0.75f, 0.75f, 0.75f, 0.9f); // brightness-75 opacity-90
                _indicatorTile.Modulate = _indicatorTile.BaseModulate; // Apply immediately
            }
        }
    }

    public void UpdateDeckCount(int count)
    {
        if (DeckCountBadge != null)
        {
            DeckCountBadge.UpdateCount(count);
        }
    }

    public void SetIndicatorTile(Tile tile)
    {
        if (_indicatorTile != null)
        {
            _indicatorTile.SetTileData(tile);
        }
    }
}
