using Godot;
using System;
using OkieRummyGodot.Core.Domain;

namespace OkieRummyGodot.UI.Scripts;

public partial class OpponentUI : HBoxContainer
{
    private Label _nameLabel;
    private Label _levelLabel;
    private PanelContainer _nameplate;
    public bool IsValidDiscardTarget { get; set; } = false;

    private ColorRect _timerRect;
    private ShaderMaterial _timerMaterial;
    private long _turnStartTime;
    private int _turnDuration;
    private bool _isTimerActive = false;

    // Standard Styles
    private StyleBoxFlat _activeStyle;
    private StyleBoxFlat _inactiveStyle;

    public override void _Ready()
    {
        _nameLabel = GetNodeOrNull<Label>("Nameplate/HBoxContainer/VBoxContainer/Name");
        _levelLabel = GetNodeOrNull<Label>("Nameplate/HBoxContainer/VBoxContainer/Level");
        _nameplate = GetNodeOrNull<PanelContainer>("Nameplate");

        _inactiveStyle = (StyleBoxFlat)_nameplate?.GetThemeStylebox("panel");

        _activeStyle = (StyleBoxFlat)_inactiveStyle?.Duplicate();
        if (_activeStyle != null)
        {
            _activeStyle.BorderColor = new Color(0.98f, 0.80f, 0.08f, 1f); // yellow-400
            _activeStyle.ShadowColor = new Color(0.98f, 0.80f, 0.08f, 0.8f);
            _activeStyle.ShadowSize = 15;
        }

        _timerRect = GetNodeOrNull<ColorRect>("Nameplate/HBoxContainer/AvatarContainer/TurnTimer");
        _timerMaterial = _timerRect?.Material as ShaderMaterial;
        _timerRect?.Hide();
    }

    public override void _Process(double delta)
    {
        if (_isTimerActive && _timerMaterial != null && _turnDuration > 0)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            float elapsed = now - _turnStartTime;
            float progress = Mathf.Clamp(elapsed / _turnDuration, 0, 1);
            _timerMaterial.SetShaderParameter("value", progress);
            
            // Dynamic color
            if (progress > 0.8f) 
                _timerMaterial.SetShaderParameter("color", new Color(1, 0.2f, 0.2f)); // Red
            else if (progress > 0.5f)
                _timerMaterial.SetShaderParameter("color", new Color(1, 0.5f, 0)); // Orange
            else
                _timerMaterial.SetShaderParameter("color", new Color(1, 0.75f, 0.18f)); // Yellow/Gold
        }
    }

    public void Initialize(Player playerData, bool isRightOpponent = false)
    {
        if (playerData == null)
        {
            Visible = false;
            return;
        }

        Visible = true;
        if (_nameLabel != null) _nameLabel.Text = playerData.Name;
        // Mocking level parsing, actual app had it typed
        if (_levelLabel != null) _levelLabel.Text = "Level 42"; 
    }

    public Control GetDropSlotNode()
    {
        return (Control)_nameplate ?? (Control)this;
    }

    public TileUI GetDiscardTileUI() => null; // To be replaced by DZ nodes logic

    public void SetActive(bool isActive)
    {
        if (_nameplate != null && _activeStyle != null && _inactiveStyle != null)
        {
            _nameplate.AddThemeStyleboxOverride("panel", isActive ? _activeStyle : _inactiveStyle);
            
            // Scaled effect
            Scale = isActive ? new Vector2(1.05f, 1.05f) : Vector2.One;

            if (!isActive) StopTimer();
        }
    }

    public void UpdateTimer(long startTime, int duration)
    {
        _turnStartTime = startTime;
        _turnDuration = duration;
        _isTimerActive = true;
        _timerRect?.Show();
    }

    public void StopTimer()
    {
        _isTimerActive = false;
        _timerRect?.Hide();
    }

    public void SetBotMode(bool isBot)
    {
        var indicator = GetNodeOrNull<Panel>("Nameplate/HBoxContainer/AvatarContainer/OnlineIndicator");
        GD.Print($"OpponentUI.SetBotMode({isBot}): indicator found={indicator != null}");
        if (indicator != null)
        {
            var style = (StyleBoxFlat)indicator.GetThemeStylebox("panel").Duplicate();
            style.BgColor = isBot ? new Color(1, 0.2f, 0.2f) : new Color(0.13f, 0.77f, 0.37f); // red or green
            indicator.AddThemeStyleboxOverride("panel", style);
        }
    }

    [Signal]
    public delegate void TileDiscardedEventHandler(int rackIndex, Control dropTarget);

    [Signal]
    public delegate void DiscardPileClickedEventHandler();
}
