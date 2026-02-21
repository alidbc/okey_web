using Godot;
using OkieRummyGodot.Core.Application;
using OkieRummyGodot.Core.Domain;
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
    private PanelContainer _localNameplate;
    private StyleBoxFlat _localActiveStyle;
    private StyleBoxFlat _localInactiveStyle;
    
    // Used to route AI turns
    private Godot.Timer _botTimer;

    public override void _Ready()
    {
        _boardCenter = GetNodeOrNull<BoardCenterUI>("CenterLayout/MiddleRow/BoardCenter");
        
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
        
        StartNewGame();
    }

    private void StartNewGame()
    {
        _matchManager = new MatchManager();
        _matchManager.OnGameStateChanged += HandleGameStateChanged;

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
            // Play sound, start animation
        }
    }

    public void OnDiscardTileDropped(int rackIndex)
    {
        if (_matchManager.DiscardTile(_localPlayer.Id, rackIndex))
        {
            // Play sound, start animation
        }
    }

    public void OnSortPressed()
    {
        _localPlayer.QuickSortRack();
        LocalRackUI?.RefreshVisuals();
    }
}
