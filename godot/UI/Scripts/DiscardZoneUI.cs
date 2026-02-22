using Godot;
using OkieRummyGodot.Core.Domain;

namespace OkieRummyGodot.UI.Scripts;

// Script attached to the Discard Pile UI to accept drops from the Player's Rack
public partial class DiscardZoneUI : PanelContainer
{
    public bool HasTile { get; set; } = false;
    public Tile CurrentTile { get; set; }
    public bool IsValidDiscardTarget { get; set; } = false;
    public bool IsInteractable { get; set; } = false;

    // Accept drops if it's a tile coming from the rack
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (!IsValidDiscardTarget) return false;

        var draggedTile = data.AsGodotObject() as TileUI;
        if (draggedTile != null)
        {
            // Only accept real tiles
            if (draggedTile.TileData == null) return false;
            
            // Check if it's from the player's rack (TileUI's parent would be a RackSlotUI)
            return draggedTile.GetParent() is RackSlotUI;
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
                // Emit the slot index and this control as the target
                EmitSignal(nameof(TileDiscarded), fromSlot.SlotIndex, this);
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!IsInteractable) return;
        if (@event is InputEventMouseButton mouseEvent && !mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
        {
            EmitSignal(nameof(DiscardPileClicked));
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!HasTile || !IsInteractable) return default;

        GD.Print("DiscardZoneUI: _GetDragData called");

        var data = new Godot.Collections.Dictionary();
        data["type"] = "drawing";
        data["fromDiscard"] = true;

        // Visual preview - Use actual TileUI for Discard Pile
        var preview = ResourceLoader.Load<PackedScene>("res://UI/Scenes/TileUI.tscn").Instantiate<TileUI>();
        preview.SetTileData(CurrentTile);
        preview.Modulate = new Color(1, 1, 1, 0.8f);
        preview.CustomMinimumSize = new Vector2(65, 90);
        preview.SetRotation(Mathf.DegToRad(-5));
        
        // Wrap it in a Control to allow offsetting
        Control wrapper = new Control();
        wrapper.AddChild(preview);
        preview.Position = -preview.CustomMinimumSize / 2;

        SetDragPreview(wrapper);

        return data;
    }

    [Signal]
    public delegate void TileDiscardedEventHandler(int rackIndex, Control dropTarget);

    [Signal]
    public delegate void DiscardPileClickedEventHandler();
}
