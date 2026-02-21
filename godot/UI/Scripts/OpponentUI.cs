using Godot;
using OkieRummyGodot.Core.Domain;

namespace OkieRummyGodot.UI.Scripts;

public partial class OpponentUI : HBoxContainer
{
    private Label _nameLabel;
    private Label _levelLabel;
    private PanelContainer _nameplate;
    private ColorRect _dropSlot;

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

    public void SetActive(bool isActive)
    {
        if (_nameplate != null && _activeStyle != null && _inactiveStyle != null)
        {
            _nameplate.AddThemeStyleboxOverride("panel", isActive ? _activeStyle : _inactiveStyle);
            
            // Scaled effect
            Scale = isActive ? new Vector2(1.05f, 1.05f) : Vector2.One;
        }
    }
}
