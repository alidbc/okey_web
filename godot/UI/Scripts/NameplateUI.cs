using Godot;
using System;

namespace OkieRummyGodot.UI.Scripts
{
    public partial class NameplateUI : PanelContainer
    {
        [Signal]
        public delegate void WinConditionDroppedEventHandler(int rackIndex, Control target);

        public bool IsInteractable { get; set; } = false;

        public void Shake()
        {
            Vector2 originalPos = Position;
            Tween tween = GetTree().CreateTween();
            
            float duration = 0.05f;
            float magnitude = 8.0f;
            
            for (int i = 0; i < 4; i++)
            {
                float offset = (i % 2 == 0) ? magnitude : -magnitude;
                tween.TweenProperty(this, "position:x", originalPos.X + offset, duration);
            }
            
            tween.TweenProperty(this, "position:x", originalPos.X, duration);
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            if (!IsInteractable) return false;

            var draggedTile = data.AsGodotObject() as TileUI;
            if (draggedTile != null)
            {
                // Only accept real tiles from the rack for win condition
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
                    GD.Print($"NameplateUI: Tile dropped for win check. RackIndex: {fromSlot.SlotIndex}");
                    EmitSignal(nameof(WinConditionDropped), fromSlot.SlotIndex, this);
                }
            }
        }
    }
}
