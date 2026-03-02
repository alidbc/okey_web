using Godot;
using System;
using System.Threading.Tasks;

namespace OkieRummyGodot.UI.Scripts
{
    public partial class NameplateUI : PanelContainer
    {
        [Signal]
        public delegate void WinConditionDroppedEventHandler(int rackIndex, Control target);

        [Signal]
        public delegate void EmoteSelectedEventHandler(string emote);

        public bool IsInteractable { get; set; } = false;
        public bool IsLocalPlayer { get; set; } = false;

        private ColorRect _timerRect;
        private ShaderMaterial _timerMaterial;
        private long _turnStartTime;
        private int _turnDuration;
        private bool _isTimerActive = false;
        
        private StyleBoxFlat _inactiveStyle;
        private StyleBoxFlat _activeStyle;
        private TextureRect _avatarRect;
        private Label _nameLabel;

        private ColorRect _shimmerOverlay;
        private Control _emoteBubble;
        private Label _emoteLabel;
        private Control _emotePicker;

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

            _avatarRect = GetNodeOrNull<TextureRect>("HBoxContainer/AvatarContainer/AvatarMask/AvatarImage");
            _nameLabel = GetNodeOrNull<Label>("HBoxContainer/VBoxContainer/Name");
            
            _shimmerOverlay = GetNodeOrNull<ColorRect>("ShimmerOverlay");
            _emoteBubble = GetNodeOrNull<Control>("EmoteBubble");
            _emoteLabel = _emoteBubble?.GetNodeOrNull<Label>("Label");
            _emoteBubble?.Hide();

            _emotePicker = GetNodeOrNull<Control>("EmotePicker");
            _emotePicker?.Hide();

            // Setup emote buttons if they exist
            if (_emotePicker != null)
            {
                foreach (var btn in _emotePicker.FindChildren("*", "Button"))
                {
                    if (btn is Button b)
                    {
                        b.Pressed += () => OnEmoteSelected(b.Text);
                        b.MouseDefaultCursorShape = CursorShape.PointingHand;
                    }
                }
            }

            // Set cursor for the whole nameplate if it's the local player
            if (IsLocalPlayer)
            {
                MouseDefaultCursorShape = CursorShape.PointingHand;
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                if (IsLocalPlayer)
                {
                    ToggleEmotePicker();
                    GetViewport().SetInputAsHandled();
                }
            }
        }

        private void ToggleEmotePicker()
        {
            if (_emotePicker == null) return;
            if (_emotePicker.Visible) _emotePicker.Hide();
            else _emotePicker.Show();
        }

        private void OnEmoteSelected(string emote)
        {
            _emotePicker?.Hide();
            EmitSignal(nameof(EmoteSelected), emote);
        }

        public void SetDisplayName(string name)
        {
            if (_nameLabel != null) _nameLabel.Text = name;
        }

        public async void SetAvatar(string url)
        {
            if (string.IsNullOrEmpty(url) || _avatarRect == null) return;
            
            // Check if it's a web URL
            if (url.StartsWith("http"))
            {
                await LoadAvatarFromUrl(url);
            }
            else if (FileAccess.FileExists(url))
            {
                _avatarRect.Texture = GD.Load<Texture2D>(url);
            }
        }

        private async Task LoadAvatarFromUrl(string url)
        {
            GD.Print($"NameplateUI: Downloading avatar from {url}");
            using var http = new HttpRequest();
            AddChild(http);
            var error = http.Request(url);
            if (error != Error.Ok)
            {
                GD.PrintErr($"NameplateUI: HttpRequest failed: {error}");
                return;
            }

            var result = await ToSignal(http, "request_completed");
            int responseCode = (int)result[1];
            var body = result[3].AsByteArray();
            
            GD.Print($"NameplateUI: Received response {responseCode}, body size {body.Length} bytes");

            if (responseCode != 200 || body.Length == 0)
            {
                GD.PrintErr("NameplateUI: Failed to download avatar or empty body");
                http.QueueFree();
                return;
            }

            var image = new Image();
            // Try PNG first, then JPG
            var imgError = image.LoadPngFromBuffer(body);
            if (imgError != Error.Ok) imgError = image.LoadJpgFromBuffer(body);
            if (imgError != Error.Ok) imgError = image.LoadWebpFromBuffer(body);
            
            if (imgError == Error.Ok)
            {
                _avatarRect.Texture = ImageTexture.CreateFromImage(image);
                GD.Print("NameplateUI: Avatar loaded successfully from buffer");
            }
            else
            {
                GD.PrintErr($"NameplateUI: Failed to parse image buffer: {imgError}");
            }
            
            http.QueueFree();
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
            if (isActive) _shimmerOverlay?.Show();
            else { _shimmerOverlay?.Hide(); StopTimer(); }
        }

        public async void ShowEmote(string text)
        {
            if (_emoteBubble == null || _emoteLabel == null) return;
            _emoteLabel.Text = text;
            _emoteBubble.Show();
            _emoteBubble.Modulate = new Color(1, 1, 1, 0);
            
            var tween = CreateTween();
            tween.TweenProperty(_emoteBubble, "modulate:a", 1.0f, 0.3f);
            tween.TweenProperty(_emoteBubble, "position:y", _emoteBubble.Position.Y - 10, 0.3f);
            
            await Task.Delay(2000);
            
            var hideTween = CreateTween();
            hideTween.TweenProperty(_emoteBubble, "modulate:a", 0.0f, 0.3f);
            await ToSignal(hideTween, "finished");
            _emoteBubble.Hide();
            _emoteBubble.Position = new Vector2(_emoteBubble.Position.X, _emoteBubble.Position.Y + 10);
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
