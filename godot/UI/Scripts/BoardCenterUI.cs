using Godot;
using OkieRummyGodot.Core.Domain;
using System;

namespace OkieRummyGodot.UI.Scripts;

public partial class BoardCenterUI : HBoxContainer
{
    [Export] public DeckUI DeckCountBadge;
    [Export] public Control IndicatorContainer;
    
    [Signal] public delegate void WinConditionDroppedEventHandler(int rackIndex, Control target);
    
    public bool IsInteractable { get; set; } = false;
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
                _indicatorTile.MouseFilter = MouseFilterEnum.Ignore;
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

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (!IsInteractable) return false;

        var draggedTile = data.AsGodotObject() as TileUI;
        if (draggedTile != null)
        {
            // Only accept real tiles from the rack
            return draggedTile.TileData != null && draggedTile.GetParent() is RackSlotUI;
        }
        return false;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var draggedTileUI = data.AsGodotObject() as TileUI;
        if (draggedTileUI != null)
        {
            var fromSlot = draggedTileUI.GetParent() as RackSlotUI;
            if (fromSlot != null)
            {
                EmitSignal(nameof(WinConditionDropped), fromSlot.SlotIndex, this);
            }
        }
    }

    public void ShakeIndicator()
    {
        if (IndicatorContainer == null) return;
        
        Vector2 originalPos = IndicatorContainer.Position;
        Tween tween = GetTree().CreateTween();
        
        // Rapid shake
        float duration = 0.05f;
        float magnitude = 8.0f;
        
        for (int i = 0; i < 4; i++)
        {
            float offset = (i % 2 == 0) ? magnitude : -magnitude;
            tween.TweenProperty(IndicatorContainer, "position:x", originalPos.X + offset, duration);
        }
        
        tween.TweenProperty(IndicatorContainer, "position:x", originalPos.X, duration);
    }
}
