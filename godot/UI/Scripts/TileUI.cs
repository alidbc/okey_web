using Godot;
using OkieRummyGodot.Core.Domain;
using System;

namespace OkieRummyGodot.UI.Scripts;

public partial class TileUI : TextureRect
{
    public Tile TileData { get; private set; }
    
    private bool _isVisualSuppressed = false;
    public bool IsVisualSuppressed 
    { 
        get => _isVisualSuppressed; 
        set 
        {
            _isVisualSuppressed = value;
            UpdateVisuals();
        }
    }

    private bool _isFullyHidden = false;
    public bool IsFullyHidden
    {
        get => _isFullyHidden;
        set
        {
            _isFullyHidden = value;
            UpdateVisuals();
        }
    }
    
    // Allows parent containers to tint the tile (like the indicator tile)
    public Color BaseModulate = new Color(1, 1, 1, 1);
    
    // Hardcoded path to the texture asset we just copied
    private Texture2D _tileTexture;
    
    private Label _numberLabel;
    private TextureRect _heartIcon;
    private TextureRect _fakeOkeyIcon;
    
    private bool _isReady = false;

    private bool _isDragging = false;
    public bool IsDragging { get => _isDragging; set => _isDragging = value; }
    private Vector2 _lastMousePos;

    public override void _Ready()
    {
        ForceReady();
        _lastMousePos = GetGlobalMousePosition();
        
        // Background texture is assigned via tscn ExtResource now, but we keep fallback just in case
        if (Texture == null)
        {
            try 
            {
                Texture = GD.Load<Texture2D>("res://Assets/Tile_v2.png");
            }
            catch (Exception) {}
        }
    }

    public override void _Process(double delta)
    {
        if (IsDragging)
        {
            var mousePos = GetGlobalMousePosition();
            var velocity = (mousePos - _lastMousePos) / (float)delta;
            _lastMousePos = mousePos;

            // Apply a subtle tilt based on horizontal velocity
            float targetRotation = Mathf.Clamp(velocity.X * 0.002f, -0.15f, 0.15f);
            Rotation = Mathf.Lerp(Rotation, targetRotation, (float)(10 * delta));
            
            // Subtle scale up when dragging
            Scale = Scale.Lerp(new Vector2(1.1f, 1.1f), (float)(10 * delta));
        }
    }

    public void PlayBounce()
    {
        var tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        Scale = new Vector2(0.8f, 0.8f);
        tween.TweenProperty(this, "scale", new Vector2(1.0f, 1.0f), 0.3f);
    }

    public void ForceReady()
    {
        if (_isReady) return;
        
        _numberLabel = GetNodeOrNull<Label>("ShadowRoot/ContentContainer/NumberLabel");
        _heartIcon = GetNodeOrNull<TextureRect>("ShadowRoot/ContentContainer/HeartIcon");
        _fakeOkeyIcon = GetNodeOrNull<TextureRect>("ShadowRoot/ContentContainer/FakeOkeyIcon");
        
        _isReady = true;
    }

    public void SetTileData(Tile tile)
    {
        ForceReady(); // Ensure nodes are populated if called before Godot's internal _Ready
        TileData = tile;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        // 0. If fully hidden, make completely transparent (used to hide source of animations)
        if (IsFullyHidden)
        {
            Modulate = new Color(BaseModulate.R, BaseModulate.G, BaseModulate.B, 0);
            if (_numberLabel != null) _numberLabel.Visible = false;
            if (_heartIcon != null) _heartIcon.Visible = false;
            if (_fakeOkeyIcon != null) _fakeOkeyIcon.Visible = false;
            return;
        }

        // 1. If suppressed (e.g. deck draw animation/drag), show background but hide face
        if (IsVisualSuppressed)
        {
            Modulate = BaseModulate;
            if (_numberLabel != null) _numberLabel.Visible = false;
            if (_heartIcon != null) _heartIcon.Visible = false;
            if (_fakeOkeyIcon != null) _fakeOkeyIcon.Visible = false;
            return;
        }

        // 2. If not suppressed and no data, it's an empty slot - hide completely
        if (TileData == null)
        {
            Modulate = new Color(BaseModulate.R, BaseModulate.G, BaseModulate.B, 0);
            if (_numberLabel != null) _numberLabel.Visible = false;
            if (_heartIcon != null) _heartIcon.Visible = false;
            if (_fakeOkeyIcon != null) _fakeOkeyIcon.Visible = false;
            return;
        }

        Modulate = BaseModulate;
        
        // Apply precise colors from React
        Color targetColor = new Color(0.13f, 0.13f, 0.13f); // Default Black #212121
        switch (TileData.Color)
        {
            case TileColor.Red:
                targetColor = new Color("#d32f2f");
                break;
            case TileColor.Blue:
                targetColor = new Color("#1976d2");
                break;
            case TileColor.Black:
                targetColor = new Color("#212121");
                break;
            case TileColor.Yellow:
                targetColor = new Color("#fbc02d");
                break;
        }

        if (TileData.IsFakeOkey)
        {
            if (_numberLabel != null) _numberLabel.Visible = false;
            if (_heartIcon != null) _heartIcon.Visible = false;
            
            if (_fakeOkeyIcon != null)
            {
                _fakeOkeyIcon.Visible = true;
                _fakeOkeyIcon.Modulate = targetColor;
            }
        }
        else
        {
            if (_fakeOkeyIcon != null) _fakeOkeyIcon.Visible = false;
            
            if (_numberLabel != null)
            {
                _numberLabel.Visible = true;
                _numberLabel.Text = TileData.Value.ToString();
                _numberLabel.AddThemeColorOverride("font_color", targetColor);
            }
            
            if (_heartIcon != null)
            {
                _heartIcon.Visible = true;
                _heartIcon.Modulate = targetColor;
            }
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (TileData == null) return default;
        
        // 1. Setup the preview visuals
        Control dragWrapper = new Control();
        var preview = ResourceLoader.Load<PackedScene>("res://UI/Scenes/TileUI.tscn").Instantiate<TileUI>();
        
        preview.SetTileData(TileData);
        preview.CustomMinimumSize = CustomMinimumSize;
        preview.Size = Size;
        preview.Modulate = new Color(1, 1, 1, 0.85f);
        preview.IsDragging = true;
        
        // Offset the preview by half its size so the mouse holds the center of the tile
        preview.Position = -Size / 2f;
        dragWrapper.AddChild(preview);
        
        SetDragPreview(dragWrapper);
        
        // 2. Hide the original tile to give the illusion of it being "picked up"
        Modulate = new Color(1, 1, 1, 0);
        
        return this; // The Variant data passed into _DropData (sending the dragging node itself)
    }

    /// <summary>
    /// Smoothly animates the tile to a global position.
    /// </summary>
    public Tween AnimateTo(Vector2 targetGlobalPos, float duration = 0.4f, float delay = 0f)
    {
        var tween = CreateTween();
        if (delay > 0) tween.SetParallel(false).TweenInterval(delay);
        
        tween.SetParallel(true);
        tween.TweenProperty(this, "global_position", targetGlobalPos, duration)
             .SetTrans(Tween.TransitionType.Back)
             .SetEase(Tween.EaseType.Out);
             
        return tween;
    }

    public override void _Notification(int what)
    {
        // Restore tile visibility if the drag successfully drops or gets cancelled
        if (what == NotificationDragEnd)
        {
            UpdateVisuals(); // This resets Modulate based on BaseModulate and TileData
            Rotation = 0;
            Scale = Vector2.One;
        }
    }
}
