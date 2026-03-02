using Godot;
using System;
using System.Threading.Tasks;
using OkieRummyGodot.Core.Domain;

namespace OkieRummyGodot.UI.Scripts;

public partial class OpponentUI : HBoxContainer
{
    private Label _nameLabel;
    private Label _levelLabel;
    private TextureRect _avatarRect;
    public NameplateUI Nameplate { get; private set; }
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
        _avatarRect = GetNodeOrNull<TextureRect>("Nameplate/HBoxContainer/AvatarContainer/AvatarMask/AvatarImage");
        Nameplate = GetNodeOrNull<NameplateUI>("Nameplate");

        _inactiveStyle = (StyleBoxFlat)Nameplate?.GetThemeStylebox("panel");

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
        SetDisplayName(playerData.Name);
        SetAvatar(playerData.AvatarUrl);
        // Mocking level parsing, actual app had it typed
        if (_levelLabel != null) _levelLabel.Text = "Level 42"; 
    }

    public void SetDisplayName(string name)
    {
        if (_nameLabel != null) _nameLabel.Text = name;
    }

    public async void SetAvatar(string url)
    {
        if (string.IsNullOrEmpty(url) || _avatarRect == null) return;
        
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
        GD.Print($"OpponentUI: Downloading avatar from {url}");
        using var http = new HttpRequest();
        AddChild(http);
        var error = http.Request(url);
        if (error != Error.Ok)
        {
            GD.PrintErr($"OpponentUI: HttpRequest failed: {error}");
            return;
        }

        var result = await ToSignal(http, "request_completed");
        int responseCode = (int)result[1];
        var body = result[3].AsByteArray();
        
        GD.Print($"OpponentUI: Received response {responseCode}, body size {body.Length} bytes");

        if (responseCode != 200 || body.Length == 0)
        {
            GD.PrintErr("OpponentUI: Failed to download avatar or empty body");
            http.QueueFree();
            return;
        }

        var image = new Image();
        // Try various formats
        var imgError = image.LoadPngFromBuffer(body);
        if (imgError != Error.Ok) imgError = image.LoadJpgFromBuffer(body);
        if (imgError != Error.Ok) imgError = image.LoadWebpFromBuffer(body);
        
        if (imgError == Error.Ok)
        {
            _avatarRect.Texture = ImageTexture.CreateFromImage(image);
            GD.Print("OpponentUI: Avatar loaded successfully");
        }
        else
        {
            GD.PrintErr($"OpponentUI: Failed to parse image buffer: {imgError}");
        }
        
        http.QueueFree();
    }

    public Control GetDropSlotNode()
    {
        return (Control)Nameplate ?? (Control)this;
    }

    public TileUI GetDiscardTileUI() => null; // To be replaced by DZ nodes logic

    public void SetActive(bool isActive)
    {
        if (Nameplate != null)
        {
            Nameplate.SetActive(isActive);
            
            // Scaled effect on the wrapper as well
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
