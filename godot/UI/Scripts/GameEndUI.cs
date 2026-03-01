using Godot;
using OkieRummyGodot.Core.Domain;
using System.Collections.Generic;

namespace OkieRummyGodot.UI.Scripts;

public partial class GameEndUI : Control
{
    [Signal] public delegate void PlayAgainEventHandler();
    [Signal] public delegate void MainMenuEventHandler();

    [Export] public Label TitleLabel;
    [Export] public Label SubtitleLabel;
    [Export] public Container TileContainer;
    [Export] public Container LeaderboardContainer;
    [Export] public PackedScene ScoreRowScene;
    [Export] public CpuParticles2D ConfettiParticles;
    [Export] public TextureRect RackBackground;
    
    private Tween _glowTween;
    private AudioEngine _audioEngine;
    
    public override void _Ready()
    {
        GD.Print("GameEndUI: _Ready called");
        _audioEngine = GetNodeOrNull<AudioEngine>("/root/AudioEngine");
        
        // Styling via code to match React glassmorphism
        var panel = GetNodeOrNull<PanelContainer>("CenterContainer/PanelContainer");
        if (panel != null)
        {
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.04f, 0.10f, 0.08f, 0.95f);
            style.CornerRadiusBottomLeft = 32;
            style.CornerRadiusBottomRight = 32;
            style.CornerRadiusTopLeft = 32;
            style.CornerRadiusTopRight = 32;
            style.BorderWidthBottom = 4;
            style.BorderWidthLeft = 4;
            style.BorderWidthRight = 4;
            style.BorderWidthTop = 4;
            style.BorderColor = new Color(0.72f, 0.53f, 0.04f); // Gold
            style.ShadowColor = new Color(0, 0, 0, 0.4f);
            style.ShadowSize = 25;
            panel.AddThemeStyleboxOverride("panel", style);
        }

        if (RackBackground != null)
        {
            RackBackground.Texture = ResourceLoader.Load<Texture2D>("res://Assets/rack.png");
            RackBackground.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            RackBackground.StretchMode = TextureRect.StretchModeEnum.Scale;
        }

        StartRadialGlow();
    }

    private void StartRadialGlow()
    {
        if (TitleLabel == null) return;
        
        _glowTween = CreateTween();
        _glowTween.SetLoops();
        _glowTween.TweenProperty(TitleLabel, "theme_override_colors/font_shadow_color", new Color(1, 0.84f, 0, 0.8f), 1.0f);
        _glowTween.Parallel().TweenProperty(TitleLabel, "theme_override_constants/shadow_outline_size", 15, 1.0f);
        _glowTween.TweenProperty(TitleLabel, "theme_override_colors/font_shadow_color", new Color(1, 0.84f, 0, 0.2f), 1.5f);
        _glowTween.Parallel().TweenProperty(TitleLabel, "theme_override_constants/shadow_outline_size", 5, 1.5f);
    }

    public void DisplayResults(List<PlayerScore> scores, bool isDeckEmpty)
    {
        GD.Print($"GameEndUI: DisplayResults called. scores={scores.Count}, isDeckEmpty={isDeckEmpty}");
        
        if (TitleLabel != null)
        {
            TitleLabel.Text = isDeckEmpty ? "GAME OVER" : "VICTORY";
            if (!isDeckEmpty && ConfettiParticles != null)
            {
                ConfettiParticles.Emitting = true;
                _audioEngine?.PlayUI("victory_fanfare"); // Optional soundtrack trigger
            }
        }
            
        if (SubtitleLabel != null)
        {
            SubtitleLabel.Visible = isDeckEmpty;
            SubtitleLabel.Text = "Deck is Empty";
        }

        // Clear old rows
        if (LeaderboardContainer != null)
        {
            foreach (Node child in LeaderboardContainer.GetChildren())
            {
                child.QueueFree();
            }

            // Add new rows
            for (int i = 0; i < scores.Count; i++)
            {
                var score = scores[i];
                var row = ScoreRowScene.Instantiate<Control>();
                LeaderboardContainer.AddChild(row);
                
                // Expecting ScoreRow to have a script with SetPlayerScore
                row.Call("SetPlayerScore", i + 1, score.PlayerName, score.Score, score.IsWinner);
            }
        }

        // Handle Winner Tiles visibility
        var handBox = GetNodeOrNull<Control>("CenterContainer/PanelContainer/MarginContainer/VBoxContainer/WinnerHandBox");
        if (handBox != null)
        {
            handBox.Visible = !isDeckEmpty;
            GD.Print($"GameEndUI: WinnerHandBox visibility set to {handBox.Visible}");
        }
        else
        {
            GD.PrintErr("GameEndUI: WinnerHandBox not found!");
        }
    }

    public void DisplayWinnerTiles(List<Tile> tiles)
    {
        GD.Print("GameEndUI: DisplayWinnerTiles triggered");
        
        if (TileContainer == null)
        {
            GD.PrintErr("GameEndUI: TileContainer is NULL!");
            return;
        }

        var row1 = TileContainer.GetNodeOrNull<Container>("Row1");
        var row2 = TileContainer.GetNodeOrNull<Container>("Row2");

        if (row1 == null || row2 == null)
        {
            GD.PrintErr("GameEndUI: Row1 or Row2 not found!");
            return;
        }

        // Clear old tiles
        foreach (Node child in row1.GetChildren()) child.QueueFree();
        foreach (Node child in row2.GetChildren()) child.QueueFree();

        if (tiles == null)
        {
            GD.Print("GameEndUI: tiles list is NULL");
            return;
        }

        GD.Print($"GameEndUI: Rendering {tiles.Count} tile slots into two rows");
        
        PackedScene tileScene = ResourceLoader.Load<PackedScene>("res://UI/Scenes/TileUI.tscn");
        if (tileScene == null)
        {
            GD.PrintErr("GameEndUI: FAILED to load res://UI/Scenes/TileUI.tscn");
            return;
        }

        // Split tiles across rows. Typically 14 tiles + 1 discard = 15 or 14 total.
        // We'll put the first 7-8 in row 1, rest in row 2.
        int row1Limit = (tiles.Count + 1) / 2;
        int tilesRendered = 0;

        for (int i = 0; i < tiles.Count; i++)
        {
            var tileData = tiles[i];
            Container targetRow = (i < row1Limit) ? row1 : row2;

            if (tileData == null)
            {
                var spacer = new Control();
                spacer.CustomMinimumSize = new Vector2(75, 104);
                targetRow.AddChild(spacer);
                continue;
            }

            try
            {
                var tileUI = tileScene.Instantiate<TileUI>();
                targetRow.AddChild(tileUI);
                tileUI.SetTileData(tileData);
                tileUI.CustomMinimumSize = new Vector2(75, 104); // Standard size
                tileUI.MouseFilter = MouseFilterEnum.Ignore;
                tilesRendered++;
            }
            catch (System.Exception e)
            {
                GD.PrintErr($"GameEndUI: Error instantiating tile: {e.Message}");
            }
        }
        
        GD.Print($"GameEndUI: Successfully rendered {tilesRendered} actual tiles");
    }

    private void OnPlayAgainPressed()
    {
        GD.Print("GameEndUI: PlayAgain button clicked");
        EmitSignal(SignalName.PlayAgain);
    }

    private void OnMainMenuPressed()
    {
        GD.Print("GameEndUI: MainMenu button clicked");
        EmitSignal(SignalName.MainMenu);
    }
}
