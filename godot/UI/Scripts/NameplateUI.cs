using Godot;
using System;

namespace OkieRummyGodot.UI.Scripts
{
    public partial class NameplateUI : PanelContainer
    {
        [Signal]
        public delegate void WinConditionDroppedEventHandler(int rackIndex, Control target);

        public bool IsInteractable { get; set; } = false;

        private ColorRect _timerRect;
        private ShaderMaterial _timerMaterial;
        private long _turnStartTime;
        private int _turnDuration;
        private bool _isTimerActive = false;
        
        private StyleBoxFlat _inactiveStyle;
        private StyleBoxFlat _activeStyle;

        public override void _Ready()
        {
            _inactiveStyle = (StyleBoxFlat)GetThemeStylebox("panel");
            _activeStyle = (StyleBoxFlat)_inactiveStyle?.Duplicate();
            if (_activeStyle != null)
            {
                _activeStyle.BorderColor = new Color(0.98f, 0.80f, 0.08f, 1f); // yellow-400
                _activeStyle.ShadowColor = new Color(0.98f, 0.80f, 0.08f, 0.8f);
                _activeStyle.ShadowSize = 15;
            }

            _timerRect = GetNodeOrNull<ColorRect>("HBoxContainer/AvatarContainer/TurnTimer");
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

        public void SetActive(bool isActive)
        {
            AddThemeStyleboxOverride("panel", isActive ? _activeStyle : _inactiveStyle);
            Scale = isActive ? new Vector2(1.05f, 1.05f) : Vector2.One;
            if (!isActive) StopTimer();
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
            var indicator = GetNodeOrNull<Panel>("HBoxContainer/AvatarContainer/OnlineIndicator");
            if (indicator != null)
            {
                var style = (StyleBoxFlat)indicator.GetThemeStylebox("panel").Duplicate();
                style.BgColor = isBot ? new Color(1, 0.2f, 0.2f) : new Color(0.13f, 0.77f, 0.37f); // red or green
                indicator.AddThemeStyleboxOverride("panel", style);
            }
        }

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
