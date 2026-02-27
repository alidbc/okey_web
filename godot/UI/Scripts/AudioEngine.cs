using Godot;
using System;
using System.Collections.Generic;

namespace OkieRummyGodot.UI.Scripts;

/// <summary>
/// Simple Audio Manager for game sound effects.
/// </summary>
public partial class AudioEngine : Node
{
    private Dictionary<string, AudioStream> _sounds = new Dictionary<string, AudioStream>();
    private AudioStreamPlayer _uiPlayer;
    private AudioStreamPlayer _gamePlayer;

    public override void _Ready()
    {
        _uiPlayer = new AudioStreamPlayer { Name = "UIPlayer", Bus = "Master" };
        _gamePlayer = new AudioStreamPlayer { Name = "GamePlayer", Bus = "Master" };
        AddChild(_uiPlayer);
        AddChild(_gamePlayer);

        // Preload common sounds if available
        LoadSound("tile_click", "res://Assets/Sounds/tile_click.wav");
        LoadSound("tile_draw", "res://Assets/Sounds/tile_draw.wav");
        LoadSound("turn_change", "res://Assets/Sounds/turn_change.wav");
        LoadSound("tile_discard", "res://Assets/Sounds/tile_discard.wav");
    }

    private void LoadSound(string name, string path)
    {
        try 
        {
            if (ResourceLoader.Exists(path))
            {
                var stream = GD.Load<AudioStream>(path);
                if (stream != null)
                {
                    _sounds[name] = stream;
                }
                else
                {
                    GD.PrintErr($"AudioEngine: GD.Load returned null for {path}");
                }
            }
            else
            {
                GD.PrintErr($"AudioEngine: Resource not found at {path}");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"AudioEngine: Exception loading {name} from {path}: {ex.Message}");
        }
    }

    public void PlayUI(string name)
    {
        if (_sounds.TryGetValue(name, out var stream))
        {
            _uiPlayer.Stream = stream;
            _uiPlayer.Play();
        }
    }

    public void PlayGame(string name)
    {
        if (_sounds.TryGetValue(name, out var stream))
        {
            _gamePlayer.Stream = stream;
            _gamePlayer.Play();
        }
    }
}
