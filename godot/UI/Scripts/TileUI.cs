using Godot;
using OkieRummyGodot.Core.Domain;
using System;

namespace OkieRummyGodot.UI.Scripts;

public partial class TileUI : TextureRect
{
    public Tile TileData { get; private set; }
    
    // Allows parent containers to tint the tile (like the indicator tile)
    public Color BaseModulate = new Color(1, 1, 1, 1);
    
    // Hardcoded path to the texture asset we just copied
    private Texture2D _tileTexture;
    
    private Label _numberLabel;
    private TextureRect _heartIcon;
    private TextureRect _fakeOkeyIcon;

    public override void _Ready()
    {
        _numberLabel = GetNodeOrNull<Label>("ShadowRoot/ContentContainer/NumberLabel");
        _heartIcon = GetNodeOrNull<TextureRect>("ShadowRoot/ContentContainer/HeartIcon");
        _fakeOkeyIcon = GetNodeOrNull<TextureRect>("ShadowRoot/ContentContainer/FakeOkeyIcon");
        
        // Background texture is assigned via tscn ExtResource now, but we keep fallback just in case
        if (Texture == null)
        {
            try 
            {
                Texture = GD.Load<Texture2D>("res://Assets/Tile.png");
            }
            catch (Exception) {}
        }
    }

    public void SetTileData(Tile tile)
    {
        TileData = tile;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (TileData == null)
        {
            Modulate = new Color(BaseModulate.R, BaseModulate.G, BaseModulate.B, 0); // Hide completely
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
        
        var preview = new TextureRect
        {
            Texture = Texture,
            ExpandMode = ExpandModeEnum.IgnoreSize,
            CustomMinimumSize = CustomMinimumSize,
            Modulate = new Color(1, 1, 1, 0.9f)
        };
        
        SetDragPreview(preview);
        return this; // Passed into _DropData
    }
}
