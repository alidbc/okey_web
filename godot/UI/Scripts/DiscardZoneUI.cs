using Godot;

namespace OkieRummyGodot.UI.Scripts;

// Script attached to the Discard Pile UI to accept drops from the Player's Rack
public partial class DiscardZoneUI : ColorRect
{
    // Accept drops if it's a tile coming from the rack
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
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
        if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
        {
            EmitSignal(nameof(DiscardPileClicked));
        }
    }

    [Signal]
    public delegate void TileDiscardedEventHandler(int rackIndex, Control dropTarget);

    [Signal]
    public delegate void DiscardPileClickedEventHandler();
}
