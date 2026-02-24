using Godot;
using System.Collections.Generic;
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
    
    [Export] public Button StartGameButton;
    [Export] public PackedScene GameEndUIScreen;
    public Core.Networking.NetworkManager NetworkManager;
    
    private bool _isMultiplayer = false;
    
    // Wire this up manually
    private BoardCenterUI _boardCenter;
    private NameplateUI _localNameplate;
    private Label _localNameLabel;
    private StyleBoxFlat _localActiveStyle;
    private StyleBoxFlat _localInactiveStyle;
    
    // Used to route AI turns
    private Godot.Timer _botTimer;

    private TileUI[] _dzTiles = new TileUI[4];
    private int _activeAnimationsCount = 0;
    
    private struct PendingDrawAnimation
    {
        public bool FromDiscard;
        public int TargetSlot;
    }
    private Queue<PendingDrawAnimation> _pendingDrawAnimations = new Queue<PendingDrawAnimation>();

    private Godot.Timer _feedbackTimer;
    private bool _isShowingFeedback = false;
    private Control _lastCheckTarget;
    private Color _originalStatusColor;
    
    private Control _leaveConfirmationPanel;
    private Button _leaveGameButton;

    public override void _Ready()
    {
        // The NetworkManager is an Autoload in Phase 3
        NetworkManager = GetNodeOrNull<Core.Networking.NetworkManager>("/root/NetworkManager");
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
        
        // MatchManager-dependent wiring moved to InitializeMultiplayer/StartNewGame
        
        _localNameplate = GetNodeOrNull<NameplateUI>("CenterLayout/BottomRow/Nameplate");
        if (_localNameplate != null)
        {
            _localNameLabel = _localNameplate.GetNodeOrNull<Label>("HBoxContainer/VBoxContainer/Name");
            _localInactiveStyle = (StyleBoxFlat)_localNameplate.GetThemeStylebox("panel");
            _localActiveStyle = (StyleBoxFlat)_localInactiveStyle?.Duplicate();
            if (_localActiveStyle != null)
            {
                _localActiveStyle.BorderColor = new Color(0.98f, 0.80f, 0.08f, 1f); // yellow-400
                _localActiveStyle.ShadowColor = new Color(0.98f, 0.80f, 0.08f, 0.8f);
                _localActiveStyle.ShadowSize = 15;
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
        // Drawing source connections are now handled in ConnectDynamicUI

        if (_boardCenter != null)
        {
            _boardCenter.Connect("WinConditionDropped", new Callable(this, nameof(OnCheckWinCondition)));
        }

        // --- DEBUG ONLY: Victory Bypass Button ---
        // (Removing debugBtn to avoid clutter, will add Leave Game button instead)
        CreateLeaveGameButton();
        CreateConfirmationDialog();
        // ------------------------------------------
    
        if (_localNameplate != null)
        {
            _localNameplate.Connect("WinConditionDropped", new Callable(this, nameof(OnCheckWinCondition)));
        }

        if (_boardCenter?.DeckCountBadge != null)
        {
            _boardCenter.DeckCountBadge.Connect("WinConditionDropped", new Callable(this, nameof(OnCheckWinCondition)));
        }

        _feedbackTimer = new Godot.Timer();
        _feedbackTimer.OneShot = true;
        _feedbackTimer.WaitTime = 3.0f;
        if (StatusLabel != null)
        {
            _originalStatusColor = StatusLabel.SelfModulate;
        }

        _feedbackTimer.Timeout += () => {
            _isShowingFeedback = false;
            if (StatusLabel != null)
            {
                StatusLabel.SelfModulate = _originalStatusColor;
            }
            HandleGameStateChanged();
        };
        AddChild(_feedbackTimer);

        if (NetworkManager != null && NetworkManager.IsActive)
        {
            _isMultiplayer = true;
            InitializeMultiplayer();
        }
        else
        {
            _isMultiplayer = false;
            StartNewGame();
        }
    }

    private void InitializeMultiplayer()
    {
        if (NetworkManager != null)
        {
            NetworkManager.BoardStateSynced += OnBoardStateSynced;
            NetworkManager.PrivateRackSynced += OnPrivateRackSynced;
            NetworkManager.WinCheckResultReceived += OnWinCheckResultReceived;
            NetworkManager.TileDrawn += OnTileDrawnSync;
            NetworkManager.TileDiscarded += OnTileDiscardedSync;
        }

        _matchManager = new MatchManager();
        // We don't start the game locally; we wait for sync
        _matchManager.OnGameStateChanged += HandleGameStateChanged;

        // Initialize players based on sync data
        if (NetworkManager.LocalPlayerIndex != -1)
        {
            // We wait for the first board sync to fully populate the player list
        }

        LocalRackUI?.Initialize(_localPlayer);
        {
            if (!LocalRackUI.IsConnected("DrawToSlot", new Callable(this, nameof(OnDrawToSlot))))
            {
                LocalRackUI.Connect("DrawToSlot", new Callable(this, nameof(OnDrawToSlot)));
            }
            if (!LocalRackUI.IsConnected("TileMoved", new Callable(this, nameof(OnRackTileMoved))))
            {
                LocalRackUI.Connect("TileMoved", new Callable(this, nameof(OnRackTileMoved)));
            }
        }

        if (StartGameButton != null)
        {
            StartGameButton.Pressed += OnStartGamePressed;
        }

        InitializeMultiplayerUI();

        ConfigureMatchManagerWiring();

        GD.Print("MainEngine: Initialized in Multiplayer mode. Requesting state sync...");
        NetworkManager.RpcId(1, nameof(Core.Networking.NetworkManager.RequestSync));
        
        ConnectDynamicUI();
    }

    private void ConnectDynamicUI()
    {
        if (_localPlayer == null) return;
        
        // Disconnect all first to be safe
        DiscardZoneUI[] dzs = { DZ1, DZ2, DZ3, DZ4 };
        foreach (var dz in dzs)
        {
            if (dz != null && dz.IsConnected("DiscardPileClicked", new Callable(this, nameof(OnDrawFromDiscardPressed))))
            {
                dz.Disconnect("DiscardPileClicked", new Callable(this, nameof(OnDrawFromDiscardPressed)));
            }
        }

        // Connect the specific source for the local player (their LEFT neighbor's discard)
        var mySource = GetDiscardSourceForPlayer(_localPlayer.Id) as DiscardZoneUI;
        if (mySource != null)
        {
            mySource.Connect("DiscardPileClicked", new Callable(this, nameof(OnDrawFromDiscardPressed)));
            GD.Print($"MainEngine: Dynamic draw source connected to {mySource.Name}");
        }
    }

    private void InitializeMultiplayerUI()
    {
        if (NetworkManager == null || _matchManager == null) return;
        int count = _matchManager.Players.Count;
        
        RightOpponent?.Initialize(_matchManager.Players.Find(p => GetRelativeIndex(p.Id) == 1), true);
        TopOpponent?.Initialize(_matchManager.Players.Find(p => GetRelativeIndex(p.Id) == 2), false);
        LeftOpponent?.Initialize(_matchManager.Players.Find(p => GetRelativeIndex(p.Id) == 3), false);
        
        if (_localPlayer != null)
        {
            LocalRackUI?.Initialize(_localPlayer);
            if (_localNameLabel != null) _localNameLabel.Text = _localPlayer.Name;
        }
    }

    private void StartNewGame()
    {
        _matchManager = new MatchManager();
        _matchManager.OnGameStateChanged += HandleGameStateChanged;
        _matchManager.OnTileDrawn += OnMatchTileDrawn;
        _matchManager.OnTileDiscarded += OnMatchTileDiscarded;

        // Initialize Players
        _localPlayer = new Player("local_user", "You", "avatar_url");
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
            LocalRackUI.Connect("TileMoved", new Callable(this, nameof(OnRackTileMoved)));
        }
        _boardCenter?.SetIndicatorTile(_matchManager.IndicatorTile);
        
        ConnectDynamicUI();
        
        // Initialize Bots in UI (Assumes specific seating: local = 0, right = 1, top = 2, left = 3)
        RightOpponent?.Initialize(bot1, true); // true = reverse layout for right
        if (DZ1 != null) DZ1.IsValidDiscardTarget = _localPlayer.Id == _matchManager.Players[_matchManager.CurrentPlayerIndex].Id && _matchManager.CurrentPhase == TurnPhase.Discard;
        if (RightOpponent != null) RightOpponent.IsValidDiscardTarget = false; // No longer a direct target
        
        TopOpponent?.Initialize(bot2, false);
        LeftOpponent?.Initialize(bot3, false);
        
        ConfigureMatchManagerWiring();

        _botTimer = new Godot.Timer();
        AddChild(_botTimer);
        _botTimer.Timeout += ProcessBotTurn;
        
        HandleGameStateChanged();
    }

    private void ConfigureMatchManagerWiring()
    {
        if (_boardCenter?.DeckCountBadge != null)
        {
            // Connect only if not already connected (or clear first)
            if (!_boardCenter.DeckCountBadge.IsConnected("DeckClicked", new Callable(this, nameof(OnDrawFromDeckPressed))))
            {
                _boardCenter.DeckCountBadge.Connect("DeckClicked", new Callable(this, nameof(OnDrawFromDeckPressed)));
            }
            _boardCenter.DeckCountBadge.TilePeeker = () => _matchManager?.PeekDeck();
        }
    }

    private void HandleGameStateChanged()
    {
        HandleGameStateChanged(false);
    }

    private void HandleGameStateChanged(bool force)
    {
        // Don't update the UI while a tile is flying, unless it's the final refresh
        if (_activeAnimationsCount > 0 && !force) return;

        if (_matchManager.Status == GameStatus.Victory || _matchManager.Status == GameStatus.GameOver)
        {
            if (StatusLabel != null) 
                StatusLabel.Text = _matchManager.Status == GameStatus.Victory ? "Victory!" : "Game Over: Deck Exhausted";
            
            ShowGameEndUI();
            return;
        }

        LocalRackUI?.RefreshVisuals();
        UpdateStatusLabel();
        if (_matchManager.GameDeck != null && _boardCenter != null)
        {
            _boardCenter.UpdateDeckCount(_matchManager.GameDeck.RemainingCount);
        }
        
        if (_matchManager.Players.Count == 0) return;
        
        var activePlayer = _matchManager.Players[_matchManager.CurrentPlayerIndex];
        
        // Highlight active player
        int activeIdx = _matchManager.CurrentPlayerIndex;
        int count = _matchManager.Players.Count;

        // Seating Visibility
        if (RightOpponent != null) RightOpponent.Visible = _matchManager.Players.Any(p => GetRelativeIndex(p.Id) == 1);
        if (TopOpponent != null) TopOpponent.Visible = _matchManager.Players.Any(p => GetRelativeIndex(p.Id) == 2);
        if (LeftOpponent != null) LeftOpponent.Visible = _matchManager.Players.Any(p => GetRelativeIndex(p.Id) == 3);

        // Highlight active seats
        RightOpponent?.SetActive(RightOpponent.Visible && activeIdx == _matchManager.Players.FindIndex(p => GetRelativeIndex(p.Id) == 1));
        TopOpponent?.SetActive(TopOpponent.Visible && activeIdx == _matchManager.Players.FindIndex(p => GetRelativeIndex(p.Id) == 2));
        LeftOpponent?.SetActive(LeftOpponent.Visible && activeIdx == _matchManager.Players.FindIndex(p => GetRelativeIndex(p.Id) == 3));


        // Manage Discard Zone Visibility: Only show zones that correspond to an active player's seating
        // DZ1 (Right), DZ2 (Top), DZ3 (Left), DZ4 (Self/Draw-Source) - wait, let's align with plan:
        // relIdx 0 (Self) -> DZ1
        // relIdx 1 (Right) -> DZ2
        // relIdx 2 (Top) -> DZ3
        // relIdx 3 (Left) -> DZ4
        // Manage Discard Zone Visibility: Only show zones that ANY player is discarding into.
        if (DZ1 != null) DZ1.Visible = _matchManager.Players.Any(p => GetDiscardTargetForPlayer(p.Id) == DZ1);
        if (DZ2 != null) DZ2.Visible = _matchManager.Players.Any(p => GetDiscardTargetForPlayer(p.Id) == DZ2);
        if (DZ3 != null) DZ3.Visible = _matchManager.Players.Any(p => GetDiscardTargetForPlayer(p.Id) == DZ3);
        if (DZ4 != null) DZ4.Visible = _matchManager.Players.Any(p => GetDiscardTargetForPlayer(p.Id) == DZ4);
        
        if (_localNameplate != null && _localActiveStyle != null && _localInactiveStyle != null)
        {
            bool isLocalTurn = activePlayer.Id == _localPlayer.Id;
            _localNameplate.AddThemeStyleboxOverride("panel", isLocalTurn ? _localActiveStyle : _localInactiveStyle);
            
            // Manage Discarding - Apply to THIS player's absolute target
            var myTarget = GetDiscardTargetForPlayer(_localPlayer.Id) as DiscardZoneUI;
            if (myTarget != null)
            {
                myTarget.IsValidDiscardTarget = isLocalTurn && _matchManager.CurrentPhase == TurnPhase.Discard;
            }

            // Manage Drawing
            bool canDraw = isLocalTurn && _matchManager.CurrentPhase == TurnPhase.Draw;

            if (_boardCenter?.DeckCountBadge != null)
                _boardCenter.DeckCountBadge.IsInteractable = canDraw;

            if (_localNameplate != null)
                _localNameplate.IsInteractable = isLocalTurn && _matchManager.CurrentPhase == TurnPhase.Discard;

            // DRAW SOURCE is dynamic
            var mySource = GetDiscardSourceForPlayer(_localPlayer.Id) as DiscardZoneUI;
            
            // Disable all first
            if (DZ1 != null) DZ1.IsInteractable = false;
            if (DZ2 != null) DZ2.IsInteractable = false;
            if (DZ3 != null) DZ3.IsInteractable = false;
            if (DZ4 != null) DZ4.IsInteractable = false;

            // Only enable my source
            if (mySource != null) mySource.IsInteractable = canDraw;

            // Manage Win Condition Check (Drag to Indicator)
            if (_boardCenter != null)
                _boardCenter.IsInteractable = isLocalTurn && _matchManager.CurrentPhase == TurnPhase.Discard;
            
            _localNameplate?.SetActive(isLocalTurn);
        }

        // Update Opponent Visuals
        RightOpponent?.SetActive(activePlayer.Id == _matchManager.Players.Find(p => GetRelativeIndex(p.Id) == 1)?.Id);
        TopOpponent?.SetActive(activePlayer.Id == _matchManager.Players.Find(p => GetRelativeIndex(p.Id) == 2)?.Id);
        LeftOpponent?.SetActive(activePlayer.Id == _matchManager.Players.Find(p => GetRelativeIndex(p.Id) == 3)?.Id);

        // Multiplayer: Manage Start Button Visibility
        if (StartGameButton != null)
        {
            bool isHost = _isMultiplayer && (NetworkManager?.IsHost() ?? false);
            bool isLobby = _matchManager.Status == GameStatus.Menu;
            StartGameButton.Visible = isHost && isLobby;
        }

        // If it's a bot's turn and we are NOT in multiplayer, trigger the timer
        if (activePlayer.IsBot && !_isMultiplayer)
        {
            _botTimer.Start(1.5f); // Bot thinking time before acting
        }
        
        UpdateDiscardVisuals();
    }

    private void UpdateStatusLabel()
    {
        if (StatusLabel == null || _isShowingFeedback) return;
        if (_matchManager == null || _matchManager.Players.Count == 0)
        {
            StatusLabel.Text = _matchManager?.Status == GameStatus.Menu ? "Waiting in Lobby..." : "Initializing...";
            return;
        }
        
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

        var dzNodes = new DiscardZoneUI[] { DZ1, DZ2, DZ3, DZ4 };
        
        // Clear all zones first
        for (int i = 0; i < 4; i++)
        {
            if (_dzTiles[i] != null) _dzTiles[i].Visible = false;
            if (dzNodes[i] != null) 
            {
                dzNodes[i].HasTile = false;
                dzNodes[i].CurrentTile = null;
            }
        }

        // Fill from active players
        foreach (var p in _matchManager.Players)
        {
            var targetZone = GetDiscardTargetForPlayer(p.Id) as DiscardZoneUI;
            if (targetZone == null) continue;

            // Find index of this targetZone in dzNodes
            int dzIdx = -1;
            for(int i=0; i<4; i++) if(dzNodes[i] == targetZone) dzIdx = i;
            if (dzIdx == -1) continue;

            _matchManager.PlayerDiscardPiles.TryGetValue(p.Id, out var pile);
            var tile = pile?.LastOrDefault();
            
            if (tile != null && _dzTiles[dzIdx] != null)
            {
                _dzTiles[dzIdx].SetTileData(tile);
                _dzTiles[dzIdx].Visible = !_dzTiles[dzIdx].IsVisualSuppressed;
                targetZone.HasTile = true;
                targetZone.CurrentTile = tile;
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

    private void OnStartGamePressed()
    {
        if (_isMultiplayer && NetworkManager != null)
        {
            NetworkManager.RpcId(1, nameof(Core.Networking.NetworkManager.RequestStartGame));
        }
    }

    // Called by UI events
    public void OnDrawFromDeckPressed()
    {
        if (_isMultiplayer)
        {
            NetworkManager.RpcId(1, nameof(Core.Networking.NetworkManager.RequestDrawFromDeck), -1);
            return;
        }

        if (_activeAnimationsCount > 0) return;
        bool isLocalTurn = _matchManager.Players[_matchManager.CurrentPlayerIndex].Id == _localPlayer.Id;
        if (!isLocalTurn || _matchManager.CurrentPhase != TurnPhase.Draw) return;
        
        int landedIndex = _matchManager.DrawFromDeck(_localPlayer.Id);
        if (landedIndex != -1)
        {
            AnimateDrawEffect(false, landedIndex, true);
        }
        else
        {
            HandleGameStateChanged(true);
        }
    }

    public void OnDrawFromDiscardPressed()
    {
        if (_isMultiplayer)
        {
            NetworkManager.RpcId(1, nameof(Core.Networking.NetworkManager.RequestDrawFromDiscard), -1);
            return;
        }

        if (_activeAnimationsCount > 0) return;
        bool isLocalTurn = _matchManager.Players[_matchManager.CurrentPlayerIndex].Id == _localPlayer.Id;
        if (!isLocalTurn || _matchManager.CurrentPhase != TurnPhase.Draw) return;
        
        int landedIndex = _matchManager.DrawFromDiscard(_localPlayer.Id);
        if (landedIndex != -1)
        {
            AnimateDrawEffect(true, landedIndex, true);
        }
        else
        {
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
        if (_isMultiplayer)
        {
            if (fromDiscard)
            {
                NetworkManager.RpcId(1, nameof(Core.Networking.NetworkManager.RequestDrawFromDiscard), targetIndex);
            }
            else
            {
                NetworkManager.RpcId(1, nameof(Core.Networking.NetworkManager.RequestDrawFromDeck), targetIndex);
            }
            // In multiplayer, wait for the server before incrementing _activeAnimationsCount and running visuals
            return;
        }

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

        Control sourceNode = fromDiscard ? GetDiscardSourceForPlayer(_localPlayer.Id) : _boardCenter?.DeckCountBadge;
        Control targetNode = LocalRackUI?.GetSlotNode(targetIndex);
        TileUI targetTileUI = LocalRackUI?.GetTileUI(targetIndex);
        Tile drawnTile = _localPlayer.Rack[targetIndex];

        if (sourceNode != null && targetNode != null && drawnTile != null)
        {
            _activeAnimationsCount++;
            
            // Set tile UI temporarily hidden before animation starts so it doesn't snap
            targetTileUI.Visible = false;
            
             AnimateTileMove(sourceNode, targetNode, drawnTile, targetTileUI, null, () => {
                targetTileUI.Visible = true;
                _activeAnimationsCount--;
                HandleGameStateChanged(true);
            });
        }
        else
        {
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

    private void OnRackTileMoved(int fromIndex, int toIndex)
    {
        if (_isMultiplayer)
        {
            NetworkManager.RpcId(1, nameof(Core.Networking.NetworkManager.RequestMoveTile), fromIndex, toIndex);
        }
    }

    public void OnDiscardTileDropped(int rackIndex, Control dropTarget = null)
    {
        if (_isMultiplayer)
        {
            NetworkManager.RpcId(1, nameof(Core.Networking.NetworkManager.RequestDiscard), rackIndex);
            return;
        }

        if (_matchManager.DiscardTile(_localPlayer.Id, rackIndex))
        {
            // Removed AnimateTileMove for local player per user request, 
            // as they are already dragging the tile physically.
            HandleGameStateChanged(true);
            LocalRackUI.RefreshVisuals();
        }
    }

    private void ShowGameEndUI()
    {
        if (GameEndUIScreen == null) return;
        
        // Check if already showing
        if (GetNodeOrNull("GameEndUI") != null) return;
        
        var endUI = GameEndUIScreen.Instantiate<GameEndUI>();
        endUI.Name = "GameEndUI";
        AddChild(endUI);
        
        var scores = _matchManager.GetPlayerScores();
        bool isDeckEmpty = _matchManager.Status == GameStatus.GameOver;
        
        endUI.DisplayResults(scores, isDeckEmpty);
        if (!isDeckEmpty)
        {
            GD.Print($"MainEngine: Displaying Winner Tiles. Count: {_matchManager.WinnerTiles?.Count ?? 0}");
            endUI.DisplayWinnerTiles(_matchManager.WinnerTiles);
        }
        endUI.PlayAgain += OnPlayAgain;
        endUI.MainMenu += OnMainMenu;
    }

    private void OnPlayAgain()
    {
        GD.Print("MainEngine: Play Again pressed. Navigating to Lobby...");
        NetworkManager?.Disconnect();
        GetTree().ChangeSceneToFile("res://UI/Scenes/Lobby.tscn");
    }

    private void OnMainMenu()
    {
        GD.Print("MainEngine: Main Menu pressed. Navigating to Lobby...");
        NetworkManager?.Disconnect();
        GetTree().ChangeSceneToFile("res://UI/Scenes/Lobby.tscn");
    }

    private void OnWinCheckResultReceived(bool success, string message)
    {
        GD.Print($"MainEngine: Win check result received: success={success}, message={message}");
        _isShowingFeedback = true;
        _feedbackTimer.Start(3.0f);
        
        if (success)
        {
            if (StatusLabel != null)
            {
                StatusLabel.Text = message;
                StatusLabel.SelfModulate = new Color(0.2f, 1.0f, 0.2f); // Green
            }
            HandleGameStateChanged(true);
        }
        else
        {
            if (StatusLabel != null)
            {
                StatusLabel.Text = $"Cannot finish: {message}";
                StatusLabel.SelfModulate = new Color(1.0f, 0.2f, 0.2f); // Red
            }
            GD.Print($"Win condition failed: {message}");
            
            // Shake the target that triggered the check
            if (_lastCheckTarget != null)
            {
                if (_lastCheckTarget.HasMethod("Shake")) _lastCheckTarget.Call("Shake");
                else if (_lastCheckTarget == _boardCenter) _boardCenter.ShakeIndicator();
            }
            else
            {
                _boardCenter?.ShakeIndicator();
            }
        }
        HandleGameStateChanged();
    }

    private void OnCheckWinCondition(int rackIndex) => OnCheckWinCondition(rackIndex, null);

    private void OnCheckWinCondition(int rackIndex, Control target)
    {
        GD.Print($"MainEngine: OnCheckWinCondition triggered. Target: {target?.Name ?? "null"}");
        _lastCheckTarget = target;
        if (_isMultiplayer)
        {
            NetworkManager.RpcId(1, nameof(Core.Networking.NetworkManager.RequestCheckWinCondition), rackIndex);
            return;
        }

        var (success, message) = _matchManager.FinishGame(_localPlayer.Id, rackIndex);
        
        _isShowingFeedback = true;
        _feedbackTimer.Start(3.0f);
        
        if (success)
        {
            if (StatusLabel != null)
            {
                StatusLabel.Text = message;
                StatusLabel.SelfModulate = new Color(0.2f, 1.0f, 0.2f); // Green
            }
            HandleGameStateChanged(true);
        }
        else
        {
            if (StatusLabel != null)
            {
                StatusLabel.Text = $"Cannot finish: {message}";
                StatusLabel.SelfModulate = new Color(1.0f, 0.2f, 0.2f); // Red
            }
            GD.Print($"Win condition failed: {message}");
            
            if (target != null)
            {
                if (target.HasMethod("Shake")) target.Call("Shake");
                else if (target == _boardCenter) _boardCenter.ShakeIndicator();
            }
            else
            {
                _boardCenter?.ShakeIndicator();
            }
        }
        HandleGameStateChanged();
    }

    private void OnMatchTileDrawn(string playerId, Tile tile, bool fromDiscard, int targetSlotIndex, bool wasDrag)
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
    }
    
    private void OnMatchTileDiscarded(string playerId, Tile tile, int rackIndex)
    {
        if (playerId == _localPlayer.Id) return;

        Control sourceNode = GetOpponentUIById(playerId);
        Control targetNode = GetDiscardTargetForPlayer(playerId);
        TileUI targetTileUI = GetDiscardTileUIAtNode(targetNode);

        if (sourceNode != null && targetNode != null)
        {
            _activeAnimationsCount++;
            AnimateTileMove(sourceNode, targetNode, tile, null, null, () => {
                _activeAnimationsCount--;
                HandleGameStateChanged(true);
            });
        }
    }

    private int GetAbsoluteIndex(string id)
    {
        if (string.IsNullOrEmpty(id)) return -1;
        if (id.StartsWith("p") && int.TryParse(id.Substring(1), out int p)) return p;
        return id switch { "local_user" => 0, "bot_1" => 1, "bot_2" => 2, "bot_3" => 3, _ => -1 };
    }

    private string GetNextPlayerId(string currentId)
    {
        int idx = _matchManager.Players.FindIndex(p => p.Id == currentId);
        if (idx == -1 || _matchManager.Players.Count == 0) return null;
        return _matchManager.Players[(idx + 1) % _matchManager.Players.Count].Id;
    }

    private string GetPrevPlayerId(string currentId)
    {
        int idx = _matchManager.Players.FindIndex(p => p.Id == currentId);
        if (idx == -1 || _matchManager.Players.Count == 0) return null;
        return _matchManager.Players[(idx - 1 + _matchManager.Players.Count) % _matchManager.Players.Count].Id;
    }

    private int GetRelativeIndex(string id)
    {
        int myIdx = _isMultiplayer ? NetworkManager.LocalPlayerIndex : 0;
        int pIdx = GetAbsoluteIndex(id);
        if (pIdx == -1) return -1;

        int count = _matchManager.Players.Count;
        if (count == 0) return -1;
        int diff = (pIdx - myIdx + count) % count;

        if (_isMultiplayer)
        {
            return count switch
            {
                2 => diff == 0 ? 0 : 2, // 0 -> Bottom, 1 -> Top (Opposite)
                3 => diff == 0 ? 0 : (diff == 1 ? 1 : 3), // 0 -> Bottom, 1 -> Right, 2 -> Left
                _ => diff // 4 players or fallback
            };
        }
        return diff;
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
        int relIdx = GetRelativeIndex(id);
        return relIdx switch {
            1 => RightOpponent,
            2 => TopOpponent,
            3 => LeftOpponent,
            _ => null
        };
    }

    private Control GetDiscardTargetForPlayer(string playerId)
    {
        int relIdx = GetRelativeIndex(playerId);
        int playerCount = _matchManager.Players.Count;

        // Special case for 2-player games:
        // We want the opponent (relIdx 2) to discard into DZ4 (Local's Left)
        // so the local player (relIdx 0) draws from their left.
        if (playerCount == 2 && relIdx == 2) return DZ4;

        return relIdx switch {
            0 => DZ1, // Local player discards to DZ1 (Right)
            1 => DZ2, // Right player discards to DZ2
            2 => DZ3, // Top player discards to DZ3
            3 => DZ4, // Left player discards to DZ4
            _ => null
        };
    }

    private Control GetDiscardSourceForPlayer(string playerId)
    {
        // Any player draws FROM the previous player's discard zone.
        string prevId = GetPrevPlayerId(playerId);
        return GetDiscardTargetForPlayer(prevId);
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
    private void OnTileDrawnSync(string playerId, bool fromDiscard, int targetSlotIndex, string tileId, int value, int color, bool wasDrag)
    {
        GD.Print($"MainEngine: OnTileDrawnSync triggered for {playerId}, fromDiscard:{fromDiscard}, targetSlot:{targetSlotIndex}, wasDrag:{wasDrag}");
        if (playerId == _localPlayer?.Id)
        {
            // Only skip the flying animation if the user already dragged it.
            if (wasDrag)
            {
                GD.Print("MainEngine: Local draw was via drag-and-drop. Skipping flying animation.");
                return;
            }

            GD.Print($"MainEngine: Queuing local draw effect until rack sync arrives");
            _pendingDrawAnimations.Enqueue(new PendingDrawAnimation { FromDiscard = fromDiscard, TargetSlot = targetSlotIndex });
        }
        else
        {
            GD.Print($"MainEngine: Animating opponent draw effect");
            AnimateOpponentDrawEffect(playerId, fromDiscard, tileId, value, color, wasDrag);
        }
    }

    private void OnTileDiscardedSync(string playerId, int previousRackIndex, string tileId, int value, int color)
    {
        GD.Print($"MainEngine: OnTileDiscardedSync triggered for {playerId}, index:{previousRackIndex}, tileId:{tileId}");
        if (playerId == _localPlayer?.Id)
        {
            GD.Print($"MainEngine: Skipping local discard effect (already animated via drag/drop)");
        }
        else
        {
            GD.Print($"MainEngine: Animating opponent discard effect");
            AnimateOpponentDiscardEffect(playerId, tileId, value, color);
        }
    }

    private void AnimateOpponentDrawEffect(string playerId, bool fromDiscard, string tileId, int value, int color, bool wasDrag)
    {
        GD.Print($"MainEngine: AnimateOpponentDrawEffect for {playerId}, count={_activeAnimationsCount}, tileId={tileId}");
        // Removed blocking to allow concurrent animations


        Control sourceNode = fromDiscard ? GetDiscardSourceForPlayer(playerId) : _boardCenter?.DeckCountBadge;
        Control targetNode = null;
        OpponentUI oppUI = GetOpponentUIForPlayer(playerId);
        if (oppUI != null)
        {
            targetNode = oppUI.GetNodeOrNull<Control>("Nameplate/HBoxContainer/AvatarContainer");
        }

        GD.Print($"MainEngine: Draw: sourceNode={sourceNode != null}, oppUI={oppUI != null}, targetNode={targetNode != null}");

        if (sourceNode != null && targetNode != null)
        {
            _activeAnimationsCount++;
            GD.Print($"MainEngine: Starting Opponent Draw Tween, count now {_activeAnimationsCount}");
            
            var animSprite = ResourceLoader.Load<PackedScene>("res://UI/Scenes/TileUI.tscn").Instantiate<TileUI>();
            animSprite.Size = new Vector2(75, 104);
            animSprite.CustomMinimumSize = new Vector2(75, 104);
            
            AddChild(animSprite);

            if (fromDiscard && !string.IsNullOrEmpty(tileId))
            {
                Tile mockData = new Tile(tileId, value, (TileColor)color);
                animSprite.SetTileData(mockData);
                animSprite.IsVisualSuppressed = false;
            }
            else
            {
                // Hide its contents initially since it's a draw from deck
                animSprite.IsVisualSuppressed = true; 
            }
            
            animSprite.GlobalPosition = sourceNode.GlobalPosition + sourceNode.Size / 2 - animSprite.Size / 2;

            var tween = CreateTween();
            tween.TweenProperty(animSprite, "global_position", targetNode.GlobalPosition + targetNode.Size / 2 - animSprite.Size / 2, 0.4f)
                 .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            tween.Parallel().TweenProperty(animSprite, "modulate:a", 0.0f, 0.4f);

            TileUI hideSource = null;
            if (fromDiscard)
            {
                var dzNodes = new DiscardZoneUI[] { DZ1, DZ2, DZ3, DZ4 };
                for (int i = 0; i < 4; i++)
                {
                    if (dzNodes[i] == sourceNode)
                    {
                        hideSource = _dzTiles[i];
                        break;
                    }
                }
            }

            if (hideSource != null) hideSource.IsFullyHidden = true;

            tween.TweenCallback(Callable.From(() => {
                GD.Print($"MainEngine: Opponent Draw Tween finished");
                if (hideSource != null) hideSource.IsFullyHidden = false;
                animSprite.QueueFree();
                _activeAnimationsCount--;
                HandleGameStateChanged(true);
            }));
        }
    }

    private void AnimateOpponentDiscardEffect(string playerId, string tileId, int value, int color)
    {
        GD.Print($"MainEngine: AnimateOpponentDiscardEffect for {playerId}, count={_activeAnimationsCount}");

        Control sourceNode = null;
        OpponentUI oppUI = GetOpponentUIForPlayer(playerId);
        if (oppUI != null)
        {
            sourceNode = oppUI.GetNodeOrNull<Control>("Nameplate/HBoxContainer/AvatarContainer");
        }

        Control targetNode = GetDiscardTargetForPlayer(playerId);
        GD.Print($"MainEngine: Discard: sourceNode={sourceNode != null}, oppUI={oppUI != null}, targetNode={targetNode != null}");

        if (sourceNode != null && targetNode != null)
        {
            _activeAnimationsCount++;
            GD.Print($"MainEngine: Starting Opponent Discard Tween, count now {_activeAnimationsCount}");
            
            Tile mockData = new Tile(tileId, value, (TileColor)color);
            
            var animSprite = ResourceLoader.Load<PackedScene>("res://UI/Scenes/TileUI.tscn").Instantiate<TileUI>();
            animSprite.Size = new Vector2(75, 104);
            animSprite.CustomMinimumSize = new Vector2(75, 104);
            
            AddChild(animSprite);
            if (mockData != null) {
                animSprite.SetTileData(mockData);
            } else {
                animSprite.IsVisualSuppressed = true;
            }
            
            animSprite.GlobalPosition = sourceNode.GlobalPosition + sourceNode.Size / 2 - animSprite.Size / 2;

            var tween = CreateTween();
            tween.TweenProperty(animSprite, "global_position", targetNode.GlobalPosition + targetNode.Size / 2 - animSprite.Size / 2, 0.4f)
                 .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

            // Don't hide the existing DZ tile — let the old top card stay visible
            // while the new card flies in on top of it. This prevents the empty-pile flash.

            tween.TweenCallback(Callable.From(() => {
                GD.Print($"MainEngine: Opponent Discard Tween finished");
                animSprite.QueueFree();
                _activeAnimationsCount--;
                HandleGameStateChanged(true);
            }));
        }
    }

    private void UpdateTurnTimers(int activePlayerIndex, long startTime, int duration)
    {
        // First, stop all timers
        _localNameplate?.StopTimer();
        TopOpponent?.StopTimer();
        LeftOpponent?.StopTimer();
        RightOpponent?.StopTimer();

        if (_matchManager.Status != GameStatus.Playing) return;

        // Start timer for the active player
        int relIdx = GetRelativeIndex($"p{activePlayerIndex}");
        switch (relIdx)
        {
            case 0:
                _localNameplate?.UpdateTimer(startTime, duration);
                break;
            case 1:
                RightOpponent?.UpdateTimer(startTime, duration);
                break;
            case 2:
                TopOpponent?.UpdateTimer(startTime, duration);
                break;
            case 3:
                LeftOpponent?.UpdateTimer(startTime, duration);
                break;
        }
    }

    private OpponentUI GetOpponentUIForPlayer(string playerId)
    {
        return GetOpponentUIById(playerId) as OpponentUI;
    }

    private void OnBoardStateSynced(string json)
    {
        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize<Core.Networking.BoardSyncData>(json);
            if (data == null) 
            {
                GD.PrintErr("MainEngine: Board sync data is null after deserialization.");
                return;
            }

            GD.Print($"MainEngine: Received Board Sync. Status: {data.Status}, Deck: {data.DeckCount}, Active: {data.ActivePlayer}");

            _matchManager.IndicatorTile = data.Indicator;
            _matchManager.CurrentPlayerIndex = data.ActivePlayer;
            _matchManager.CurrentPhase = data.Phase;
            _matchManager.Status = data.Status == "Active" ? GameStatus.Playing : (data.Status == "Victory" ? GameStatus.Victory : GameStatus.Menu);
            _matchManager.WinnerId = data.WinnerId;
            _matchManager.WinnerTiles = data.WinnerTiles;

            if (_matchManager.Status == GameStatus.Victory)
            {
                // Delay slightly to allow the final sync to settle if needed
                GetTree().CreateTimer(0.5f).Timeout += ShowGameEndUI;
            }
            
            // Critical: Ensure player list is populated and local player is identified
            if (_matchManager.Players.Count != data.Discards.Count)
            {
                GD.Print($"MainEngine: Player refresh needed ({_matchManager.Players.Count} vs {data.Discards.Count})");
                _matchManager.Players.Clear();
                _matchManager.PlayerDiscardPiles.Clear();
                
                for (int i = 0; i < data.Discards.Count; i++)
                {
                    string name = (data.PlayerNames != null && i < data.PlayerNames.Count) ? data.PlayerNames[i] : (i == NetworkManager.LocalPlayerIndex ? "You" : $"Player {i + 1}");
                    var p = new Player($"p{i}", name, "");
                    _matchManager.AddPlayer(p);
                    if (i == NetworkManager.LocalPlayerIndex) _localPlayer = p;
                }
                InitializeMultiplayerUI();
                ConnectDynamicUI(); // Re-connect signals for the new local player
            }
            else if (_localPlayer == null && NetworkManager.LocalPlayerIndex != -1 && NetworkManager.LocalPlayerIndex < _matchManager.Players.Count)
            {
                _localPlayer = _matchManager.Players[NetworkManager.LocalPlayerIndex];
                InitializeMultiplayerUI();
                ConnectDynamicUI();
            }
            else if (data.PlayerNames != null && data.PlayerNames.Count == _matchManager.Players.Count)
            {
                // Update names if they changed (e.g. from "Player X" to peer ID)
                bool changed = false;
                for (int i = 0; i < data.PlayerNames.Count; i++)
                {
                    if (_matchManager.Players[i].Name != data.PlayerNames[i])
                    {
                        _matchManager.Players[i].Name = data.PlayerNames[i];
                        changed = true;
                    }
                }
                if (changed) InitializeMultiplayerUI();
            }

            // Update Turn Timers
            UpdateTurnTimers(data.ActivePlayer, data.TurnStartTimestamp, data.TurnDuration);

            // Update Bot / Disconnected Visuals
            if (data.IsBot != null && data.IsBot.Count == _matchManager.Players.Count)
            {
                for (int i = 0; i < data.IsBot.Count; i++)
                {
                    bool isBot = data.IsBot[i];
                    bool isDisconnected = data.IsDisconnected != null && i < data.IsDisconnected.Count && data.IsDisconnected[i];
                    bool showRed = isBot || isDisconnected;
                    int relIdx = GetRelativeIndex($"p{i}");
                    switch (relIdx)
                    {
                        case 0:
                            _localNameplate?.SetBotMode(showRed);
                            break;
                        case 1:
                            RightOpponent?.SetBotMode(showRed);
                            break;
                        case 2:
                            TopOpponent?.SetBotMode(showRed);
                            break;
                        case 3:
                            LeftOpponent?.SetBotMode(showRed);
                            break;
                    }
                }
            }

            if (_localPlayer != null && _localNameLabel != null)
            {
                _localNameLabel.Text = _localPlayer.Name;
            }

            // Apply discards
            for (int i = 0; i < data.Discards.Count; i++)
            {
                var pid = $"p{i}";
                _matchManager.PlayerDiscardPiles[pid] = data.Discards[i];
            }
            
            _matchManager.GameDeck = new Deck { RemainingCount = data.DeckCount };
            
            // Update Indicator UI
            if (_boardCenter != null)
            {
                _boardCenter.SetIndicatorTile(data.Indicator);
                _boardCenter.UpdateDeckCount(data.DeckCount);
            }

            // Always ensure UI signals are connected if they aren't
            ConnectDynamicUI();

            // Prevent multiplayer state sync from snapping tiles mid-animation
            if (_activeAnimationsCount > 0)
            {
                GD.Print("MainEngine: Animation active, skipping instant visual refresh from Board Sync.");
                return;
            }

            var activeP = _matchManager.Players[_matchManager.CurrentPlayerIndex];
            GD.Print($"MainEngine: Final State Check - LocalPlayerIndex: {NetworkManager.LocalPlayerIndex}, LocalPlayerID: {_localPlayer?.Id ?? "null"}, ActivePlayerIndex: {_matchManager.CurrentPlayerIndex}, ActivePlayerID: {activeP.Id}");

            HandleGameStateChanged(true);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MainEngine: Error in OnBoardStateSynced: {ex.Message}\nJSON: {json}");
        }
    }

    private void OnPrivateRackSynced(string json)
    {
        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize<Core.Networking.RackSyncData>(json);
            if (data == null || _localPlayer == null) 
            {
                GD.PrintErr($"MainEngine: Rack sync failed. Data={data == null}, Player={_localPlayer == null}");
                return;
            }

            GD.Print($"MainEngine: Received Rack Sync. Tiles: {data.Slots.Count}");
            _matchManager.NextDeckTileHint = data.NextDeckTile;
            
            for (int i = 0; i < data.Slots.Count && i < _localPlayer.Rack.Length; i++)
            {
                _localPlayer.Rack[i] = data.Slots[i];
            }

            while (_pendingDrawAnimations.Count > 0)
            {
                var anim = _pendingDrawAnimations.Dequeue();
                AnimateDrawEffect(anim.FromDiscard, anim.TargetSlot, true);
            }

            // In multiplayer, the Draw animation needs the data to be there but we shouldn't snap it.
            // RefreshVisuals is skipped if an animation is active via HandleGameStateChanged(true).
            if (_activeAnimationsCount == 0)
            {
                LocalRackUI?.RefreshVisuals();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MainEngine: Error in OnPrivateRackSynced: {ex.Message}\nJSON: {json}");
        }
    }

    private void CreateLeaveGameButton()
    {
        _leaveGameButton = new Button();
        _leaveGameButton.Text = "Leave Game";
        _leaveGameButton.CustomMinimumSize = new Vector2(120, 40);
        
        // Style it to match the theme (reddish/brownish for exit)
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.6f, 0.2f, 0.2f, 0.8f); // Dark red transparent
        style.CornerRadiusBottomLeft = 8;
        style.CornerRadiusBottomRight = 8;
        style.CornerRadiusTopLeft = 8;
        style.CornerRadiusTopRight = 8;
        style.BorderWidthBottom = 2;
        style.BorderWidthLeft = 2;
        style.BorderWidthRight = 2;
        style.BorderWidthTop = 2;
        style.BorderColor = new Color(0.4f, 0.1f, 0.1f, 1f);
        
        _leaveGameButton.AddThemeStyleboxOverride("normal", style);
        
        var hoverStyle = (StyleBoxFlat)style.Duplicate();
        hoverStyle.BgColor = new Color(0.8f, 0.3f, 0.3f, 0.9f);
        _leaveGameButton.AddThemeStyleboxOverride("hover", hoverStyle);

        _leaveGameButton.SetPosition(new Vector2(20, 20));
        _leaveGameButton.Pressed += () => {
            if (_leaveConfirmationPanel != null) _leaveConfirmationPanel.Visible = true;
        };
        
        AddChild(_leaveGameButton);
    }

    private void CreateConfirmationDialog()
    {
        _leaveConfirmationPanel = new Panel();
        _leaveConfirmationPanel.CustomMinimumSize = new Vector2(400, 200);
        _leaveConfirmationPanel.Visible = false;
        
        var glassStyle = new StyleBoxFlat();
        glassStyle.BgColor = new Color(0.04f, 0.10f, 0.08f, 0.95f);
        glassStyle.CornerRadiusBottomLeft = 16;
        glassStyle.CornerRadiusBottomRight = 16;
        glassStyle.CornerRadiusTopLeft = 16;
        glassStyle.CornerRadiusTopRight = 16;
        glassStyle.BorderWidthBottom = 2;
        glassStyle.BorderWidthLeft = 2;
        glassStyle.BorderWidthRight = 2;
        glassStyle.BorderWidthTop = 2;
        glassStyle.BorderColor = new Color(1f, 1f, 1f, 0.1f);
        glassStyle.ShadowColor = new Color(0, 0, 0, 0.5f);
        glassStyle.ShadowSize = 20;

        _leaveConfirmationPanel.AddThemeStyleboxOverride("panel", glassStyle);
        
        // Center it
        _leaveConfirmationPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        
        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddThemeConstantOverride("separation", 30);
        _leaveConfirmationPanel.AddChild(vbox);

        var label = new Label();
        label.Text = "Are you sure you want to leave the game?";
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(label);

        var hbox = new HBoxContainer();
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        hbox.AddThemeConstantOverride("separation", 40);
        vbox.AddChild(hbox);

        var leaveBtn = new Button();
        leaveBtn.Text = "Leave";
        leaveBtn.CustomMinimumSize = new Vector2(100, 40);
        leaveBtn.Pressed += OnConfirmLeave;
        hbox.AddChild(leaveBtn);

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancel";
        cancelBtn.CustomMinimumSize = new Vector2(100, 40);
        cancelBtn.Pressed += () => _leaveConfirmationPanel.Visible = false;
        hbox.AddChild(cancelBtn);

        AddChild(_leaveConfirmationPanel);
    }

    private void OnConfirmLeave()
    {
        GD.Print("MainEngine: Leave confirmed. Resetting session...");
        NetworkManager?.Disconnect();
        GetTree().ChangeSceneToFile("res://UI/Scenes/Lobby.tscn");
    }
}
