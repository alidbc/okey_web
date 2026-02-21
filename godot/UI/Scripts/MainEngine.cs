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
    
    // Wire this up manually since NodePaths in C# export often break if modified externally
    private BoardCenterUI _boardCenter;
    private DiscardZoneUI _discardZone;
    private PanelContainer _localNameplate;
    private StyleBoxFlat _localActiveStyle;
    private StyleBoxFlat _localInactiveStyle;
    
    // Used to route AI turns
    private Godot.Timer _botTimer;

    private TileUI _localDiscardTile;

    public override void _Ready()
    {
        _boardCenter = GetNodeOrNull<BoardCenterUI>("CenterLayout/MiddleRow/BoardCenter");
        _discardZone = GetNodeOrNull<DiscardZoneUI>("CenterLayout/PlayerElementsRow/DiscardPileTarget");
        
        if (_discardZone != null)
        {
            _discardZone.Connect("TileDiscarded", new Callable(this, nameof(OnDiscardTileDropped)));
            
            // Spawn a visual tile inside the discard zone
            _localDiscardTile = ResourceLoader.Load<PackedScene>("res://UI/Scenes/TileUI.tscn").Instantiate<TileUI>();
            _localDiscardTile.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _localDiscardTile.SizeFlagsVertical = SizeFlags.ExpandFill;
            _localDiscardTile.CustomMinimumSize = Vector2.Zero;
            _localDiscardTile.Visible = false;
            _discardZone.AddChild(_localDiscardTile);
        }
        
        if (_boardCenter?.DeckCountBadge != null)
        {
            _boardCenter.DeckCountBadge.Connect("DeckClicked", new Callable(this, nameof(OnDrawFromDeckPressed)));
        }
        
        _localNameplate = GetNodeOrNull<PanelContainer>("CenterLayout/PlayerElementsRow/Nameplate");
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
        if (_discardZone != null) 
        {
            _discardZone.Connect("DiscardPileClicked", new Callable(this, nameof(OnDrawFromDiscardPressed)));
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
        _boardCenter?.SetIndicatorTile(_matchManager.IndicatorTile);
        
        // Initialize Bots in UI (Assumes specific seating: local = 0, right = 1, top = 2, left = 3)
        RightOpponent?.Initialize(bot1, true); // true = reverse layout for right
        TopOpponent?.Initialize(bot2, false);
        LeftOpponent?.Initialize(bot3, false);
        
        _botTimer = new Godot.Timer();
        AddChild(_botTimer);
        _botTimer.Timeout += ProcessBotTurn;
        
        HandleGameStateChanged();
    }

    private void HandleGameStateChanged()
    {
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
        // OKEY MAPPING LOGIC:
        // You draw from the player to your LEFT.
        // You discard for the player on your RIGHT.

        // 1. Local Player's "Draw Pile" (Left side of screen) = Bot 3's discards
        if (_matchManager.PlayerDiscardPiles.TryGetValue("bot_3", out var leftPlayerPile) && leftPlayerPile.Count > 0)
        {
            _localDiscardTile?.SetTileData(leftPlayerPile[^1]);
            if (_localDiscardTile != null) _localDiscardTile.Visible = true;
            // Also update the Left Bot's avatar slot just in case
            LeftOpponent?.SetDiscardTile(leftPlayerPile[^1]);
        }
        else if (_localDiscardTile != null)
        {
            _localDiscardTile.Visible = false;
            LeftOpponent?.SetDiscardTile(null);
        }
        
        // 2. Right Opponent's Slot = Local Player's discards (Bot 1 draws from Local)
        if (_matchManager.PlayerDiscardPiles.TryGetValue(_localPlayer.Id, out var myPile) && myPile.Count > 0)
            RightOpponent?.SetDiscardTile(myPile[^1]);
        else
            RightOpponent?.SetDiscardTile(null);
            
        // 3. Top Opponent's Slot = Right Opponent's discards (Bot 2 draws from Bot 1)
        if (_matchManager.PlayerDiscardPiles.TryGetValue("bot_1", out var rightPile) && rightPile.Count > 0)
            TopOpponent?.SetDiscardTile(rightPile[^1]);
        else
            TopOpponent?.SetDiscardTile(null);
            
        // 4. Left Opponent's Slot = Top Opponent's discards (Bot 3 draws from Bot 2)
        if (_matchManager.PlayerDiscardPiles.TryGetValue("bot_2", out var topPile) && topPile.Count > 0)
            LeftOpponent?.SetDiscardTile(topPile[^1]);
        // Note: we already used bot_3 discards for the local _discardZone above.
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
        if (_matchManager.DrawFromDeck(_localPlayer.Id))
        {
            // TODO: Animate from Deck down to Rack
            HandleGameStateChanged();
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
        if (fromDiscard)
        {
            // Find who is to the left of this player
            sourceNode = GetDiscardSourceForPlayer(playerId);
        }
        else
        {
            sourceNode = _boardCenter?.DeckCountBadge;
        }

        Control targetNode = GetOpponentUIById(playerId);

        if (sourceNode != null && targetNode != null)
        {
            AnimateTileMove(sourceNode, targetNode, tile, null);
        }
    }

    private void OnMatchTileDiscarded(string playerId, Tile tile)
    {
        if (playerId == _localPlayer.Id) return;

        Control sourceNode = GetOpponentUIById(playerId);
        Control targetNode = GetDiscardTargetForPlayer(playerId);

        if (sourceNode != null && targetNode != null)
        {
            AnimateTileMove(sourceNode, targetNode, tile, null);
        }
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
        if (playerId == "bot_1") return _discardZone; // bot_1 draws from local discard
        if (playerId == "bot_2") return RightOpponent?.GetDropSlotNode(); // bot_2 draws from bot_1
        if (playerId == "bot_3") return TopOpponent?.GetDropSlotNode(); // bot_3 draws from bot_2
        if (playerId == "local_user") return LeftOpponent?.GetDropSlotNode(); // local draws from bot_3
        return null;
    }

    private Control GetDiscardTargetForPlayer(string playerId)
    {
        // playerId discards to the player to their right
        if (playerId == "bot_1") return TopOpponent?.GetDropSlotNode(); // bot_1 discards to bot_2
        if (playerId == "bot_2") return LeftOpponent?.GetDropSlotNode(); // bot_2 discards to bot_3
        if (playerId == "bot_3") return _discardZone; // bot_3 discards to local draw pile
        if (playerId == "local_user") return RightOpponent?.GetDropSlotNode(); // local discards to bot_1
        return null;
    }

    public void OnDrawFromDiscardPressed()
    {
        if (_matchManager.DrawFromDiscard(_localPlayer.Id))
        {
            // TODO: Animate from Left Player's Discard down to Rack
            HandleGameStateChanged();
        }
    }

    public void OnSortPressed()
    {
        _localPlayer.QuickSortRack();
        LocalRackUI?.RefreshVisuals();
    }
    
    // --- Global Animation Manager ---
    private void AnimateTileMove(Control sourceControl, Control targetControl, Tile tileData, Action onComplete)
    {
        if (sourceControl == null || targetControl == null || tileData == null)
        {
            onComplete?.Invoke();
            return;
        }

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
        if (targetControl == _discardZone)
        {
            tween.Parallel().TweenProperty(ghostTile, "scale", new Vector2(0.9f, 0.9f), 0.4f);
        }

        tween.Finished += () => {
            ghostTile.QueueFree();
            onComplete?.Invoke();
        };
    }
}
