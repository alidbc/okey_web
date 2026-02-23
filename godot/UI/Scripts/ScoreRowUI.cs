using Godot;
using System;

namespace OkieRummyGodot.UI.Scripts;

public partial class ScoreRowUI : HBoxContainer
{
    private Label _rankLabel;
    private Label _crownLabel;
    private Label _nameLabel;
    private Label _scoreLabel;
    private PanelContainer _background;

    public override void _Ready()
    {
        _rankLabel = GetNodeOrNull<Label>("Rank");
        _crownLabel = GetNodeOrNull<Label>("Crown");
        _nameLabel = GetNodeOrNull<Label>("Name");
        _scoreLabel = GetNodeOrNull<Label>("Score");
        _background = GetNodeOrNull<PanelContainer>("Background");
    }

    public void SetPlayerScore(int rank, string name, int score, bool isWinner)
    {
        if (_rankLabel != null) _rankLabel.Text = rank.ToString();
        if (_crownLabel != null) _crownLabel.Text = isWinner ? "👑" : "";
        if (_nameLabel != null)
        {
            _nameLabel.Text = name;
            _nameLabel.Modulate = isWinner ? new Color("#facc15") : new Color("#ffffff"); // yellow-400
        }
        if (_scoreLabel != null)
        {
            _scoreLabel.Text = $"{score} pts";
            _scoreLabel.Modulate = isWinner ? new Color("#facc15") : new Color("#d1d5db"); // gray-300
        }

        if (_background != null)
        {
            var style = new StyleBoxFlat();
            if (isWinner)
            {
                style.BgColor = new Color(0.79f, 0.45f, 0.13f, 0.3f); // yellow-600/30
                style.BorderWidthBottom = 2;
                style.BorderWidthLeft = 2;
                style.BorderWidthRight = 2;
                style.BorderWidthTop = 2;
                style.BorderColor = new Color("#eab308"); // yellow-500
            }
            else
            {
                style.BgColor = new Color(1, 1, 1, 0.05f); // white/5
            }
            style.CornerRadiusBottomLeft = 8;
            style.CornerRadiusBottomRight = 8;
            style.CornerRadiusTopLeft = 8;
            style.CornerRadiusTopRight = 8;
            _background.AddThemeStyleboxOverride("panel", style);
        }
    }
}
