using Godot;
using OkieRummyGodot.Core.Application;
using OkieRummyGodot.Core.Domain;
using System;
using System.Linq;

namespace OkieRummyGodot.UI.Scripts;

public partial class MainEngine : Control
{
    private MatchManager _matchManager;
    private Player _localPlayer;

    [Export] public RackUI LocalRackUI;
    [Export] public Label StatusLabel;
    
    [Export] public OpponentUI TopOpponent;
    [Export] public OpponentUI LeftOpponent;
    [Export] public OpponentUI RightOpponent;

    [Export] public DiscardZoneUI DZ1; // Local to Right
    [Export] public DiscardZoneUI DZ2; // Right to Top
    [Export] public DiscardZoneUI DZ3; // Top to Left
    [Export] public DiscardZoneUI DZ4; // Left to Local
    
    // Wire this up manually
    private BoardCenterUI _boardCenter;
    private PanelContainer _localNameplate;
    private StyleBoxFlat _localActiveStyle;
    private StyleBoxFlat _localInactiveStyle;
    
    // Used to route AI turns
    private Godot.Timer _botTimer;

    private TileUI[] _dzTiles = new TileUI[4];
    private int _activeAnimationsCount = 0;

    public override void _Ready()
    {
        _boardCenter = GetNodeOrNull<BoardCenterUI>("CenterLayout/MiddleRow/BoardCenter");
        
        // Initialize DZ Tiles
        DiscardZoneUI[] dzs = { DZ1, DZ2, DZ3, DZ4 };
        PackedScene tileScene = ResourceLoader.Load<PackedScene>("res://UI/Scenes/TileUI.tscn");

        for (int i = 0; i < 4; i++)
        {
            if (dzs[i] == null) continue;
            
            dzs[i].Connect("TileDiscarded", new Callable(this, nameof(OnDiscardTileDropped)));
            
            _dzTiles[i] = tileScene.Instantiate<TileUI>();
            _dzTiles[i].SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _dzTiles[i].SizeFlagsVertical = SizeFlags.ExpandFill;
            _dzTiles[i].CustomMinimumSize = Vector2.Zero;
            _dzTiles[i].Visible = false;
            _dzTiles[i].MouseFilter = MouseFilterEnum.Ignore;
            dzs[i].AddChild(_dzTiles[i]);
        }
        
        if (DZ1 != null) DZ1.IsValidDiscardTarget = true;
        
        if (_boardCenter?.DeckCountBadge != null)
        {
            _boardCenter.DeckCountBadge.Connect("DeckClicked", new Callable(this, nameof(OnDrawFromDeckPressed)));
            _boardCenter.DeckCountBadge.TilePeeker = () => _matchManager.PeekDeck();
        }
        
        _localNameplate = GetNodeOrNull<PanelContainer>("CenterLayout/BottomRow/Nameplate");
        if (_localNameplate != null)
        {
            _localInactiveStyle = (StyleBoxFlat)_localNameplate.GetThemeStylebox("panel");
            _localActiveStyle = (StyleBoxFlat)_localInactiveStyle?.Duplicate();
            if (_localActiveStyle != null)
            {
                _localActiveStyle.BorderColor = new Color(0.98f, 0.80f, 0.08f, 1f); // yellow-400
                _localActiveStyle.ShadowColor = new Color(0.98f, 0.80f, 0.08f, 0.8f);
                _localActiveStyle.ShadowSize = 25;
            }
        }
        
        // Manual wiring if exports fail
        TopOpponent ??= GetNodeOrNull<OpponentUI>("TopOpponentArea/TopOpponent");
        LeftOpponent ??= GetNodeOrNull<OpponentUI>("CenterLayout/MiddleRow/LeftOpponent");
        RightOpponent ??= GetNodeOrNull<OpponentUI>("CenterLayout/MiddleRow/RightOpponent");
        
        // Massive UX Improvement: Dropping a tile on ANY Opponent forwards it as a discard
        if (TopOpponent != null) TopOpponent.Connect("TileDiscarded", new Callable(this, nameof(OnDiscardTileDropped)));
        if (LeftOpponent != null) LeftOpponent.Connect("TileDiscarded", new Callable(this, nameof(OnDiscardTileDropped)));
        if (RightOpponent != null) RightOpponent.Connect("TileDiscarded", new Callable(this, nameof(OnDiscardTileDropped)));

        // In Okey, you draw from the player on your Left.
        // Visually, the player's discard zone sits on the left side of the screen.
        // You click "your" left discard pile to draw the tile *they* gave to *you*.
        if (DZ4 != null) 
        {
            DZ4.Connect("DiscardPileClicked", new Callable(this, nameof(OnDrawFromDiscardPressed)));
        }

        StartNewGame();
    }

    private void StartNewGame()
    {
        _matchManager = new MatchManager();
        _matchManager.OnGameStateChanged += HandleGameStateChanged;
        _matchManager.OnTileDrawn += OnMatchTileDrawn;
        _matchManager.OnTileDiscarded += OnMatchTileDiscarded;

        // Initialize Players
        _localPlayer = new Player("local_user", "OkeyPro_99", "avatar_url");
        _matchManager.AddPlayer(_localPlayer);
        
        var bot1 = new BotPlayer("bot_1", "Elena", "", _matchManager);
        var bot2 = new BotPlayer("bot_2", "Victor", "", _matchManager);
        var bot3 = new BotPlayer("bot_3", "Marcus", "", _matchManager);
        
        _matchManager.AddPlayer(bot1);
        _matchManager.AddPlayer(bot2);
        _matchManager.AddPlayer(bot3);

        _matchManager.StartGame();
        
        LocalRackUI?.Initialize(_localPlayer);
        if (LocalRackUI != null)
        {
            LocalRackUI.Connect("DrawToSlot", new Callable(this, nameof(OnDrawToSlot)));
        }
        _boardCenter?.SetIndicatorTile(_matchManager.IndicatorTile);
        
        // Initialize Bots in UI (Assumes specific seating: local = 0, right = 1, top = 2, left = 3)
        RightOpponent?.Initialize(bot1, true); // true = reverse layout for right
        if (DZ1 != null) DZ1.IsValidDiscardTarget = _localPlayer.Id == _matchManager.Players[_matchManager.CurrentPlayerIndex].Id && _matchManager.CurrentPhase == TurnPhase.Discard;
        if (RightOpponent != null) RightOpponent.IsValidDiscardTarget = false; // No longer a direct target
        
        TopOpponent?.Initialize(bot2, false);
        LeftOpponent?.Initialize(bot3, false);
        
        _botTimer = new Godot.Timer();
        AddChild(_botTimer);
        _botTimer.Timeout += ProcessBotTurn;
        
        HandleGameStateChanged();
    }

    private void HandleGameStateChanged()
    {
        HandleGameStateChanged(false);
    }

    private void HandleGameStateChanged(bool force)
    {
        // Don't update the UI while a tile is flying, unless it's the final refresh
        if (_activeAnimationsCount > 0) return;

        if (_matchManager.Status == GameStatus.Victory)
        {
            if (StatusLabel != null) StatusLabel.Text = "Victory! Someone won!";
            return;
        }

        LocalRackUI?.RefreshVisuals();
        UpdateStatusLabel();
        _boardCenter?.UpdateDeckCount(_matchManager.GameDeck.RemainingCount);
        
        // Highlight active opponent
        var activePlayer = _matchManager.Players[_matchManager.CurrentPlayerIndex];
        RightOpponent?.SetActive(activePlayer.Id == "bot_1");
        TopOpponent?.SetActive(activePlayer.Id == "bot_2");
        LeftOpponent?.SetActive(activePlayer.Id == "bot_3");
        
        if (_localNameplate != null && _localActiveStyle != null && _localInactiveStyle != null)
        {
            bool isLocalTurn = activePlayer.Id == _localPlayer.Id;
            _localNameplate.AddThemeStyleboxOverride("panel", isLocalTurn ? _localActiveStyle : _localInactiveStyle);
            
            // Manage Discarding
            if (DZ1 != null) DZ1.IsValidDiscardTarget = isLocalTurn && _matchManager.CurrentPhase == TurnPhase.Discard;

            // Manage Drawing
            bool canDraw = isLocalTurn && _matchManager.CurrentPhase == TurnPhase.Draw;

            if (_boardCenter?.DeckCountBadge != null)
                _boardCenter.DeckCountBadge.IsInteractable = canDraw;

            // DZ4 is the local player's draw source (the pile to their left)
            if (DZ4 != null) DZ4.IsInteractable = canDraw;

            // Reset interaction on other DZs just in case
            if (DZ2 != null) DZ2.IsInteractable = false;
            if (DZ3 != null) DZ3.IsInteractable = false;
            // DZ1 is only for discarding, not for drawing for the local player.
            if (DZ1 != null) DZ1.IsInteractable = false;
        }

        // If it's a bot's turn, trigger the timer
        if (activePlayer.IsBot)
        {
            _botTimer.Start(1.5f); // Bot thinking time before acting
        }
        
        UpdateDiscardVisuals();
    }

    private void UpdateStatusLabel()
    {
        if (StatusLabel == null) return;
        
        var activePlayer = _matchManager.Players[_matchManager.CurrentPlayerIndex];
        if (activePlayer.Id == _localPlayer.Id)
        {
            StatusLabel.Text = _matchManager.CurrentPhase == TurnPhase.Draw ? "Your Turn: Draw a tile" : "Your Turn: Discard a tile";
        }
        else
        {
            StatusLabel.Text = $"{activePlayer.Name} is playing...";
        }
    }

    private void UpdateDiscardVisuals()
    {
        if (_matchManager == null) return;

        // Circular flow: 
        // Local discards to DZ1. Bot1 draws from DZ1.
        // Bot1 discards to DZ2. Bot2 draws from DZ2.
        // Bot2 discards to DZ3. Bot3 draws from DZ3.
        // Bot3 discards to DZ4. Local draws from DZ4.
        
        var dzNodes = new DiscardZoneUI[] { DZ1, DZ2, DZ3, DZ4 };
        string[] playerIds = { "local_user", "bot_1", "bot_2", "bot_3" };

        for (int i = 0; i < 4; i++)
        {
            string pid = playerIds[i];
            _matchManager.PlayerDiscardPiles.TryGetValue(pid, out var pile);
            var tile = pile?.LastOrDefault();
            
            if (_dzTiles[i] != null)
            {
                if (tile == null) 
                {
                    _dzTiles[i].Visible = false;
                    if (dzNodes[i] != null)
                    {
                        dzNodes[i].HasTile = false;
                        dzNodes[i].CurrentTile = null;
                    }
                }
                else
                {
                    _dzTiles[i].SetTileData(tile);
                    _dzTiles[i].Visible = !_dzTiles[i].IsVisualSuppressed;
                    if (dzNodes[i] != null)
                    {
                        dzNodes[i].HasTile = true;
                        dzNodes[i].CurrentTile = tile;
                    }
                }
            }
        }
    }

    private async void ProcessBotTurn()
    {
        _botTimer.Stop(); // Ensure it only runs once per trigger
        
        var activePlayer = _matchManager.Players[_matchManager.CurrentPlayerIndex] as BotPlayer;
        if (activePlayer != null)
        {
            await activePlayer.PlayTurnAsync();
        }
    }

    // Called by UI events
    public void OnDrawFromDeckPressed()
    {
        _activeAnimationsCount++;
        int landedIndex = _matchManager.DrawFromDeck(_localPlayer.Id);
        if (landedIndex != -1)
        {
            AnimateDrawEffect(false, landedIndex, true);
        }
        else
        {
            _activeAnimationsCount--;
            HandleGameStateChanged(true);
        }
    }

    public void OnDrawFromDiscardPressed()
    {
        _activeAnimationsCount++;
        int landedIndex = _matchManager.DrawFromDiscard(_localPlayer.Id);
        if (landedIndex != -1)
        {
            AnimateDrawEffect(true, landedIndex, true);
        }
        else
        {
            _activeAnimationsCount--;
            HandleGameStateChanged(true);
        }
    }

    private void OnDrawToSlot(bool fromDiscard, int targetIndex)
    {
        // Internal helper: by default, if we come from a Slot drop, we don't re-animate
        OnDrawToSlot(fromDiscard, targetIndex, false);
    }

    private void OnDrawToSlot(bool fromDiscard, int targetIndex, bool animate)
    {
        if (animate) _activeAnimationsCount++;

        int landedIndex = -1;
        if (fromDiscard)
        {
            landedIndex = _matchManager.DrawFromDiscard(_localPlayer.Id, targetIndex);
        }
        else
        {
            landedIndex = _matchManager.DrawFromDeck(_localPlayer.Id, targetIndex);
        }

        if (landedIndex != -1)
        {
            AnimateDrawEffect(fromDiscard, landedIndex, animate);
        }
        else if (animate)
        {
            _activeAnimationsCount--;
            HandleGameStateChanged(true);
        }
    }

    private int GetFirstAvailableSlotIndex()
    {
        for (int i = 0; i < _localPlayer.Rack.Length; i++)
        {
            if (_localPlayer.Rack[i] == null) return i;
        }
        return -1;
    }

    private void AnimateDrawEffect(bool fromDiscard, int targetIndex, bool animate)
    {
        if (!animate)
        {
            HandleGameStateChanged();
            return;
        }

        Control sourceNode = fromDiscard ? DZ4 : _boardCenter?.DeckCountBadge;
        Control targetNode = LocalRackUI?.GetSlotNode(targetIndex);
        TileUI targetTileUI = LocalRackUI?.GetTileUI(targetIndex);
        Tile drawnTile = _localPlayer.Rack[targetIndex];

        if (sourceNode != null && targetNode != null && drawnTile != null)
        {
             AnimateTileMove(sourceNode, targetNode, drawnTile, targetTileUI, null, () => {
                _activeAnimationsCount--;
                HandleGameStateChanged(true);
            });
        }
        else
        {
            _activeAnimationsCount--;
            HandleGameStateChanged(true);
        }
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        var dict = data.AsGodotDictionary();
        if (dict != null && dict.ContainsKey("type") && dict["type"].AsString() == "drawing")
        {
            return true;
        }
        return false;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var dict = data.AsGodotDictionary();
        if (dict != null && dict.ContainsKey("type") && dict["type"].AsString() == "drawing")
        {
            bool fromDiscard = dict["fromDiscard"].AsBool();
            // User dropped on background, use first available slot but ANIMATE it
            OnDrawToSlot(fromDiscard, -1, true);
        }
    }

    public void OnDiscardTileDropped(int rackIndex, Control dropTarget = null)
    {
        if (_matchManager.DiscardTile(_localPlayer.Id, rackIndex))
        {
            // Removed AnimateTileMove for local player per user request, 
            // as they are already dragging the tile physically.
            HandleGameStateChanged();
            LocalRackUI.RefreshVisuals();
        }
    }

    private void OnMatchTileDrawn(string playerId, Tile tile, bool fromDiscard)
    {
        if (playerId == _localPlayer.Id) return;

        Control sourceNode = null;
        TileUI sourceTileUI = null;
        if (fromDiscard)
        {
            // Find who is to the left of this player
            sourceNode = GetDiscardSourceForPlayer(playerId);
            sourceTileUI = GetDiscardTileUIAtNode(sourceNode);
        }
        else
        {
            sourceNode = _boardCenter?.DeckCountBadge;
        }

        Control targetNode = GetOpponentUIById(playerId);

        if (sourceNode != null && targetNode != null)
        {
            _activeAnimationsCount++;
            AnimateTileMove(sourceNode, targetNode, tile, null, sourceTileUI, () => {
                _activeAnimationsCount--;
                HandleGameStateChanged(true);
            });
        }
    }

    private void OnMatchTileDiscarded(string playerId, Tile tile)
    {
        if (playerId == _localPlayer.Id) return;

        Control sourceNode = GetOpponentUIById(playerId);
        Control targetNode = GetDiscardTargetForPlayer(playerId);
        TileUI targetTileUI = GetDiscardTileUIAtNode(targetNode);

        if (sourceNode != null && targetNode != null)
        {
            _activeAnimationsCount++;
            AnimateTileMove(sourceNode, targetNode, tile, targetTileUI, null, () => {
                _activeAnimationsCount--;
                HandleGameStateChanged(true);
            });
        }
    }

    private TileUI GetDiscardTileUIAtNode(Control node)
    {
        if (node == DZ1) return _dzTiles[0];
        if (node == DZ2) return _dzTiles[1];
        if (node == DZ3) return _dzTiles[2];
        if (node == DZ4) return _dzTiles[3];
        return null;
    }

    private Control GetOpponentUIById(string id)
    {
        if (id == "bot_1") return RightOpponent;
        if (id == "bot_2") return TopOpponent;
        if (id == "bot_3") return LeftOpponent;
        return null; // Local
    }

    private Control GetDiscardSourceForPlayer(string playerId)
    {
        // playerId draws from the player to their left
        if (playerId == "bot_1") return DZ4; // bot_1 draws from LZ (provided by bot_3 or local? wait)
        // Correction on flow:
        // Local discards to DZ1. Bot1 draws from DZ1.
        // Bot1 discards to DZ2. Bot2 draws from DZ2.
        // Bot2 discards to DZ3. Bot3 draws from DZ3.
        // Bot3 discards to DZ4. Local draws from DZ4.
        
        if (playerId == "bot_1") return DZ1; 
        if (playerId == "bot_2") return DZ2; 
        if (playerId == "bot_3") return DZ3; 
        if (playerId == "local_user") return DZ4; 
        return null;
    }

    private Control GetDiscardTargetForPlayer(string playerId)
    {
        // playerId discards to the player to their right
        if (playerId == "bot_1") return DZ2; 
        if (playerId == "bot_2") return DZ3; 
        if (playerId == "bot_3") return DZ4; 
        if (playerId == "local_user") return DZ1; 
        return null;
    }

    public void OnSortPressed()
    {
        _localPlayer.QuickSortRack();
        LocalRackUI?.RefreshVisuals();
    }
    
    // --- Global Animation Manager ---
    private void AnimateTileMove(Control sourceControl, Control targetControl, Tile tileData, Action onComplete)
    {
        AnimateTileMove(sourceControl, targetControl, tileData, null, null, onComplete);
    }

    private void AnimateTileMove(Control sourceControl, Control targetControl, Tile tileData, TileUI suppressTarget, TileUI hideSource, Action onComplete)
    {
        if (sourceControl == null || targetControl == null || tileData == null)
        {
            onComplete?.Invoke();
            return;
        }

        // Suppress the target if requested
        if (suppressTarget != null) suppressTarget.IsVisualSuppressed = true;
        // Hide the source if requested (to make it look like it's truly moving)
        if (hideSource != null) hideSource.IsVisualSuppressed = true;

        // 1. Create a dummy TileUI to act as the flying ghost
        var ghostTile = ResourceLoader.Load<PackedScene>("res://UI/Scenes/TileUI.tscn").Instantiate<TileUI>();
        AddChild(ghostTile);
        
        // Setup ghost visuals
        ghostTile.SetTileData(tileData);
        ghostTile.SizeFlagsHorizontal = 0;
        ghostTile.SizeFlagsVertical = 0;
        
        // 2. Calculate global positions
        Vector2 startPos = sourceControl.GlobalPosition;
        Vector2 targetPos = targetControl.GlobalPosition;
        
        // Ensure ghost matches the size of the destination
        ghostTile.CustomMinimumSize = targetControl.Size;
        ghostTile.Size = targetControl.Size;
        ghostTile.GlobalPosition = startPos;
        
        // 3. Create the Tween
        Tween tween = GetTree().CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic);
        tween.SetEase(Tween.EaseType.Out);
        
        // Fly over 0.4 seconds
        tween.TweenProperty(ghostTile, "global_position", targetPos, 0.4f);
        
        // Shrink slightly if throwing into discard
        if (targetControl is DiscardZoneUI)
        {
            tween.Parallel().TweenProperty(ghostTile, "scale", new Vector2(0.9f, 0.9f), 0.4f);
        }

        tween.Finished += () => {
            if (suppressTarget != null) suppressTarget.IsVisualSuppressed = false;
            if (hideSource != null) hideSource.IsVisualSuppressed = false;
            ghostTile.QueueFree();
            onComplete?.Invoke();
        };
    }
}
