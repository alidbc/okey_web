using Godot;
using OkieRummyGodot.Core.Domain;

namespace OkieRummyGodot.UI.Scripts;

public partial class OpponentUI : HBoxContainer
{
    private Label _nameLabel;
    private Label _levelLabel;
    private PanelContainer _nameplate;
    private ColorRect _dropSlot;
    private TileUI _discardTileUI;
    public bool IsValidDiscardTarget { get; set; } = false;

    // Standard Styles
    private StyleBoxFlat _activeStyle;
    private StyleBoxFlat _inactiveStyle;

    public override void _Ready()
    {
        _nameLabel = GetNodeOrNull<Label>("Nameplate/HBoxContainer/VBoxContainer/Name");
        _levelLabel = GetNodeOrNull<Label>("Nameplate/HBoxContainer/VBoxContainer/Level");
        _nameplate = GetNodeOrNull<PanelContainer>("Nameplate");
        _dropSlot = GetNodeOrNull<ColorRect>("DropSlot");

        _inactiveStyle = (StyleBoxFlat)_nameplate?.GetThemeStylebox("panel");

        _activeStyle = (StyleBoxFlat)_inactiveStyle?.Duplicate();
        if (_activeStyle != null)
        {
            _activeStyle.BorderColor = new Color(0.98f, 0.80f, 0.08f, 1f); // yellow-400
            _activeStyle.ShadowColor = new Color(0.98f, 0.80f, 0.08f, 0.8f);
            _activeStyle.ShadowSize = 25;
        }

        if (_dropSlot != null)
        {
            _discardTileUI = ResourceLoader.Load<PackedScene>("res://UI/Scenes/TileUI.tscn").Instantiate<TileUI>();
            _discardTileUI.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _discardTileUI.SizeFlagsVertical = SizeFlags.ExpandFill;
            _discardTileUI.CustomMinimumSize = Vector2.Zero;
            _discardTileUI.Visible = false;
            _dropSlot.AddChild(_discardTileUI);
        }
    }

    public void Initialize(Player playerData, bool isRightOpponent = false)
    {
        if (_nameLabel != null) _nameLabel.Text = playerData.Name;
        // Mocking level parsing, actual app had it typed
        if (_levelLabel != null) _levelLabel.Text = "Level 42"; 

        if (isRightOpponent)
        {
            // React app does `flex-row-reverse` for right opponents.
            // In Godot, we reverse the children order inside HBoxContainer.
            if (_dropSlot != null && _nameplate != null)
            {
                MoveChild(_dropSlot, 0); // Put drop slot before nameplate
            }
        }
    }

    public Control GetDropSlotNode()
    {
        if (_dropSlot != null) return (Control)_dropSlot;
        return (Control)this;
    }

    public TileUI GetDiscardTileUI() => _discardTileUI;

    public void SetActive(bool isActive)
    {
        if (_nameplate != null && _activeStyle != null && _inactiveStyle != null)
        {
            _nameplate.AddThemeStyleboxOverride("panel", isActive ? _activeStyle : _inactiveStyle);
            
            // Scaled effect
            Scale = isActive ? new Vector2(1.05f, 1.05f) : Vector2.One;
        }
    }

    public void SetDiscardTile(Tile tile)
    {
        if (_discardTileUI == null) return;
        
        if (tile == null)
        {
            _discardTileUI.Visible = false;
        }
        else
        {
            _discardTileUI.SetTileData(tile);
            _discardTileUI.Visible = true;
        }
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (!IsValidDiscardTarget) return false;
        
        var draggedTile = data.AsGodotObject() as TileUI;
        if (draggedTile != null)
        {
            if (draggedTile.TileData == null) return false;
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
