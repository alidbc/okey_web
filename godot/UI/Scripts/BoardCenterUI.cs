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

    public void GlowIndicator()
    {
        if (IndicatorContainer == null) return;

        // Reset any previous state
        IndicatorContainer.Scale = Vector2.One;
        IndicatorContainer.Modulate = new Color(1, 1, 1, 1);

        Tween tween = GetTree().CreateTween();
        tween.SetParallel(true);
        tween.SetTrans(Tween.TransitionType.Back);
        tween.SetEase(Tween.EaseType.Out);

        // Punch out and glow golden
        tween.TweenProperty(IndicatorContainer, "scale", new Vector2(1.25f, 1.25f), 0.3f);
        tween.TweenProperty(IndicatorContainer, "modulate", new Color(1.5f, 1.3f, 0.5f, 1f), 0.3f); // Over-brightness for glow effect

        // Settle back
        tween.SetParallel(false);
        tween.TweenProperty(IndicatorContainer, "scale", Vector2.One, 0.4f).SetTrans(Tween.TransitionType.Cubic);
        tween.Parallel().TweenProperty(IndicatorContainer, "modulate", new Color(1, 1, 1, 1), 0.4f);

        // Flash the underlying tile face if it's visible
        if (_indicatorTile != null)
        {
            _indicatorTile.BaseModulate = new Color(1, 1, 1, 1); // Remove dimming when shown
        }
    }
}
