using Godot;

namespace OkieRummyGodot.UI.Scripts;

// A script attached to each panel in the Rack to handle Godot native drag and drop
public partial class RackSlotUI : PanelContainer
{
    public int SlotIndex { get; set; }
    
    // Accept drops if the dragged data is a TileUI AND the tile actually possesses data
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        var draggedTile = data.AsGodotObject() as TileUI;
        if (draggedTile != null)
        {
            return draggedTile.TileData != null;
        }
        return false;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var draggedTileUI = data.AsGodotObject() as TileUI;
        if (draggedTileUI != null)
        {
            // Find where this tile came from
            var fromSlot = draggedTileUI.GetParent() as RackSlotUI;
            if (fromSlot != null && fromSlot.SlotIndex != SlotIndex)
            {
                // Emit signal upwards for the MatchManager/MainEngine to process the data swap
                EmitSignal(nameof(TileMoved), fromSlot.SlotIndex, SlotIndex);
            }
        }
    }

    [Signal]
    public delegate void TileMovedEventHandler(int fromIndex, int toIndex);
}
