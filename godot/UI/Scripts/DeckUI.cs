using Godot;
using System;

namespace OkieRummyGodot.UI.Scripts;

public partial class DeckUI : PanelContainer
{
    private Label _countLabel;

    public override void _Ready()
    {
        _countLabel = GetNodeOrNull<Label>("CountBadge/Label");
        
        // Setup circle styling
        var circle = GetNodeOrNull<Panel>("CircleContainer/Circle");
        if (circle != null)
        {
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0, 0, 0, 0);
            style.BorderWidthBottom = 2;
            style.BorderWidthLeft = 2;
            style.BorderWidthRight = 2;
            style.BorderWidthTop = 2;
            style.BorderColor = new Color(0.85f, 0.44f, 0.15f, 0.5f); // amber-600/50
            style.CornerRadiusBottomLeft = 16;
            style.CornerRadiusBottomRight = 16;
            style.CornerRadiusTopLeft = 16;
            style.CornerRadiusTopRight = 16;
            circle.AddThemeStyleboxOverride("panel", style);
        }
        
        // Setup badge styling
        var badge = GetNodeOrNull<PanelContainer>("CountBadge");
        if (badge != null)
        {
            var badgeStyle = new StyleBoxFlat();
            badgeStyle.BgColor = new Color("#dc2626"); // red-600
            badgeStyle.CornerRadiusBottomLeft = 12;
            badgeStyle.CornerRadiusBottomRight = 12;
            badgeStyle.CornerRadiusTopLeft = 12;
            badgeStyle.CornerRadiusTopRight = 12;
            badgeStyle.BorderWidthBottom = 1;
            badgeStyle.BorderWidthTop = 1;
            badgeStyle.BorderWidthLeft = 1;
            badgeStyle.BorderWidthRight = 1;
            badgeStyle.BorderColor = new Color("#f87171"); // red-400
            badgeStyle.ShadowColor = new Color(0, 0, 0, 0.2f);
            badgeStyle.ShadowSize = 2;
            
            // Offset to match -top-3 -right-3
            badge.Position = new Vector2(53, -10);
            badge.AddThemeStyleboxOverride("panel", badgeStyle);
        }
    }

    public void UpdateCount(int count)
    {
        if (_countLabel != null)
        {
            _countLabel.Text = count.ToString();
        }
    }
}
