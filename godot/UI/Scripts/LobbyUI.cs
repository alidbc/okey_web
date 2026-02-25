using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OkieRummyGodot.Core.Domain;
using OkieRummyGodot.Core.Application;

namespace OkieRummyGodot.UI.Scripts
{
    public partial class LobbyUI : Control
    {
        [Export] public LineEdit RoomCodeInput;
        [Export] public Label StatusLabel;
        [Export] public Button HostButton;
        [Export] public Button JoinButton;
        [Export] public Button OfflineButton;
        [Export] public Button BrowseButton;
        [Export] public string MainGameScenePath = "res://UI/Scenes/Main.tscn";
        [Export] public Control RoomListContainer;
        [Export] public Control FriendsListContainer;
        [Export] public LineEdit AddFriendInput;
        [Export] public Button AddFriendButton;
        [Export] public Button FriendsTab;
        [Export] public Button RequestsTab;
        [Export] public Control RequestsListContainer;
        [Export] public ScrollContainer RequestsScrollContainer;
        [Export] public ScrollContainer FriendsScrollContainer;
        [Export] public Button SettingsButton;

        private Core.Networking.NetworkManager _networkManager;

        private bool _isHosting = false;
        private bool _isBrowsing = false;
        private string _pendingRoomCode = "";
        private bool _isInRoom = false;
        private bool _isTestHost = false;
        private bool _isTestJoin = false;

        public override void _Ready()
        {
            ApplyGlassmorphism();
            
            _networkManager = GetNodeOrNull<Core.Networking.NetworkManager>("/root/NetworkManager");
            
            if (_networkManager != null)
            {
                _networkManager.ConnectionSuccessful += OnConnectionSuccessful;
                _networkManager.ConnectionFailed += OnConnectionFailed;
                _networkManager.RoomError += OnRoomError;
                _networkManager.RoomListReceived += OnRoomListReceived;
                _networkManager.RoomCreated += OnRoomCreated;
                _networkManager.RoomJoined += OnRoomJoined;
            }

            HostButton.Pressed += OnHostPressed;
            JoinButton.Pressed += OnJoinPressed;
            OfflineButton.Pressed += OnOfflinePressed;
            if (BrowseButton != null) BrowseButton.Pressed += OnBrowsePressed;
            if (AddFriendButton != null) AddFriendButton.Pressed += OnAddFriendPressed;

            // Tab switching
            if (FriendsTab != null) FriendsTab.Pressed += () => SwitchTab("friends");
            if (RequestsTab != null) RequestsTab.Pressed += () => SwitchTab("requests");
            if (SettingsButton != null) SettingsButton.Pressed += OnSettingsPressed;

            LoadFriends();

            // --- Automated Test Mode ---
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            if (args.Contains("--test-marathon-host"))
            {
                _isTestHost = true;
                GD.Print("LobbyUI: [TEST] Marathon Host mode detected. Auto-connecting...");
                CallDeferred(nameof(RunAutoTest));
            }
            else if (args.Contains("--test-marathon-join"))
            {
                _isTestJoin = true;
                GD.Print("LobbyUI: [TEST] Marathon Join mode detected. Auto-connecting...");
                CallDeferred(nameof(RunAutoTest));
            }
        }

        public override void _ExitTree()
        {
            if (_networkManager != null)
            {
                _networkManager.ConnectionSuccessful -= OnConnectionSuccessful;
                _networkManager.ConnectionFailed -= OnConnectionFailed;
                _networkManager.RoomError -= OnRoomError;
                _networkManager.RoomListReceived -= OnRoomListReceived;
                _networkManager.RoomCreated -= OnRoomCreated;
                _networkManager.RoomJoined -= OnRoomJoined;
            }
        }

        private void ApplyGlassmorphism()
        {
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.04f, 0.10f, 0.08f, 0.9f);
            style.CornerRadiusBottomLeft = 24;
            style.CornerRadiusBottomRight = 24;
            style.CornerRadiusTopLeft = 24;
            style.CornerRadiusTopRight = 24;
            style.BorderWidthBottom = 2;
            style.BorderWidthLeft = 2;
            style.BorderWidthRight = 2;
            style.BorderWidthTop = 2;
            style.BorderColor = new Color(0.72f, 0.53f, 0.04f, 0.3f);
            style.ShadowColor = new Color(0, 0, 0, 0.3f);
            style.ShadowSize = 15;

            var roomPanel = GetNodeOrNull<PanelContainer>("MarginContainer/MainLayout/RoomManagement");
            roomPanel?.AddThemeStyleboxOverride("panel", style);

            var friendsPanel = GetNodeOrNull<PanelContainer>("MarginContainer/MainLayout/FriendsPanel");
            friendsPanel?.AddThemeStyleboxOverride("panel", style);
        }

        private void OnBrowsePressed()
        {
            _isBrowsing = true;
            _isHosting = false;
            StatusLabel.Text = "Fetching available rooms...";
            _networkManager?.ConnectToServer("127.0.0.1", 8080);
        }

        private void OnHostPressed()
        {
            _isHosting = true;
            _pendingRoomCode = "";
            StatusLabel.Text = "Connecting to server...";
            _networkManager?.ConnectToServer("127.0.0.1", 8080);
        }

        private void OnJoinPressed()
        {
            _isHosting = false;
            _pendingRoomCode = RoomCodeInput.Text.Trim();
            if (string.IsNullOrEmpty(_pendingRoomCode))
            {
                StatusLabel.Text = "Please enter a room code.";
                return;
            }

            StatusLabel.Text = $"Joining room {_pendingRoomCode}...";
            _networkManager?.ConnectToServer("127.0.0.1", 8080);
        }

        private void OnOfflinePressed()
        {
            _networkManager?.Disconnect();
            GetTree().ChangeSceneToFile(MainGameScenePath);
        }

        private void OnConnectionSuccessful()
        {
            if (_isBrowsing)
            {
                StatusLabel.Text = "Refreshing room list...";
                _networkManager?.RefreshRoomList();
            }
            else if (_isHosting)
            {
                StatusLabel.Text = "Creating room...";
                _networkManager?.CreateRoomOnServer();
            }
            else
            {
                StatusLabel.Text = "Joining room...";
                _networkManager?.JoinRoomOnServer(_pendingRoomCode);
            }
        }

        private void OnRoomListReceived(string json)
        {
            var data = System.Text.Json.JsonSerializer.Deserialize<Core.Networking.RoomListSyncData>(json);
            if (data == null || RoomListContainer == null) return;

            foreach (Node child in RoomListContainer.GetChildren())
                child.QueueFree();

            if (data.Rooms.Count == 0)
            {
                StatusLabel.Text = "No public rooms found.";
                return;
            }

            StatusLabel.Text = $"Found {data.Rooms.Count} rooms.";

            foreach (var room in data.Rooms)
            {
                Button btn = new Button();
                btn.Text = $"Room {room.Code} ({room.PlayerCount}/4) - {room.Status}";
                btn.CustomMinimumSize = new Vector2(0, 45);
                btn.Pressed += () => JoinSpecificRoom(room.Code);
                RoomListContainer.AddChild(btn);
            }
        }

        private void JoinSpecificRoom(string code)
        {
            _isHosting = false;
            _isBrowsing = false;
            _pendingRoomCode = code;
            StatusLabel.Text = $"Joining room {code}...";
            _networkManager?.JoinRoomOnServer(code);
        }

        private async void OnAddFriendPressed()
        {
            string name = AddFriendInput?.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
            if (accountMgr?.Supabase == null || !accountMgr.Supabase.IsAuthenticated)
            {
                StatusLabel.Text = "Sign in to add friends";
                return;
            }

            var friendService = new Core.Networking.FriendService(accountMgr.Supabase);
            var results = await friendService.SearchPlayers(name);
            if (results.Count > 0)
            {
                bool sent = await friendService.SendFriendRequest(results[0].PlayerId);
                StatusLabel.Text = sent ? $"Friend request sent to {results[0].DisplayName}" : "Failed to send request";
            }
            else
            {
                StatusLabel.Text = "Player not found";
            }
            AddFriendInput.Text = "";
        }

        private async void LoadFriends()
        {
            var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
            if (accountMgr?.Supabase == null || !accountMgr.Supabase.IsAuthenticated)
            {
                // Not signed in — show empty or local placeholder
                return;
            }

            // Start presence heartbeat
            _presenceService = new Core.Networking.PresenceService(accountMgr.Supabase);
            _presenceService.Start();

            var friendService = new Core.Networking.FriendService(accountMgr.Supabase);
            var friends = await friendService.GetFriends();
            RefreshFriendsList(friends);
        }

        private Core.Networking.PresenceService _presenceService;

        private void RefreshFriendsList(List<Core.Networking.FriendInfo> friends = null)
        {
            if (FriendsListContainer == null) return;

            foreach (Node child in FriendsListContainer.GetChildren())
                child.QueueFree();

            if (friends == null || friends.Count == 0)
            {
                var emptyLabel = new Label {
                    Text = "No friends yet. Search by name above!",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    CustomMinimumSize = new Vector2(0, 40)
                };
                emptyLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
                FriendsListContainer.AddChild(emptyLabel);
                return;
            }

            foreach (var friend in friends)
            {
                HBoxContainer entry = new HBoxContainer();
                entry.CustomMinimumSize = new Vector2(0, 40);
                entry.AddThemeConstantOverride("separation", 8);

                // Status dot
                var statusDot = new Panel();
                statusDot.CustomMinimumSize = new Vector2(10, 10);
                var dotStyle = new StyleBoxFlat();
                dotStyle.SetCornerRadiusAll(5);
                switch (friend.OnlineStatus)
                {
                    case "online": dotStyle.BgColor = new Color(0.13f, 0.77f, 0.37f); break;
                    case "in_game": dotStyle.BgColor = new Color(0.3f, 0.5f, 1.0f); break;
                    case "away": dotStyle.BgColor = new Color(1.0f, 0.8f, 0.0f); break;
                    default: dotStyle.BgColor = new Color(0.4f, 0.4f, 0.4f); break;
                }
                statusDot.AddThemeStyleboxOverride("panel", dotStyle);
                statusDot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                entry.AddChild(statusDot);

                // Name
                Label nameLabel = new Label {
                    Text = friend.DisplayName,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill
                };
                entry.AddChild(nameLabel);

                // Invite button
                Button inviteBtn = new Button {
                    Text = "INVITE",
                    CustomMinimumSize = new Vector2(60, 0)
                };
                inviteBtn.ThemeTypeVariation = "ButtonSmall";
                var friendId = friend.PlayerId;
                var friendName = friend.DisplayName;
                inviteBtn.Pressed += () => InviteFriendToRoom(friendId, friendName);
                entry.AddChild(inviteBtn);

                FriendsListContainer.AddChild(entry);
            }
        }

        private void OnRoomCreated(string code)
        {
            _isInRoom = true;
            StatusLabel.Text = $"Room Created: {code}";
            UpdateRoomUIForWaitingState();
        }

        private void OnRoomJoined(string code, int playerIndex)
        {
            _isInRoom = true;
            StatusLabel.Text = $"Joined Room: {code}";
            UpdateRoomUIForWaitingState();

            // Auto-test: host adds bots and starts
            if (_isTestHost)
            {
                GD.Print($"LobbyUI: [TEST] Joined room {code} as host. Adding bots and starting...");
                CallDeferred(nameof(AutoAddBotsAndStart));
            }
            else if (_isTestJoin)
            {
                GD.Print($"LobbyUI: [TEST] Joined room {code} as joiner (index {playerIndex}). Waiting for host to start.");
            }
        }

        private void UpdateRoomUIForWaitingState()
        {
            if (!_isInRoom) return;

            // Update Public Section to show "Room Players"
            var subtitle = GetNodeOrNull<Label>("MarginContainer/MainLayout/RoomManagement/VBoxContainer/PublicSection/Label");
            if (subtitle != null) subtitle.Text = "WAITING FOR PLAYERS...";

            if (BrowseButton != null && _networkManager.LocalPlayerIndex == 0)
            {
                BrowseButton.Text = "ADD BOT";
                BrowseButton.Pressed -= OnBrowsePressed;
                BrowseButton.Pressed += () => _networkManager.RpcId(1, "RequestAddBot");
            }

            if (_networkManager.LocalPlayerIndex == 0)
            {
                HostButton.Text = "START GAME";
                HostButton.Pressed -= OnHostPressed;
                HostButton.Pressed += () => _networkManager.RpcId(1, "RequestStartGame");
            }
            else
            {
                HostButton.Disabled = true;
                HostButton.Text = "WAITING FOR HOST...";
            }
        }

        private void OnRoomError(string message)
        {
            StatusLabel.Text = $"Error: {message}";
        }

        private void OnConnectionFailed()
        {
            StatusLabel.Text = "Failed to connect to server.";
            if (_isTestHost || _isTestJoin)
                GD.PrintErr("LobbyUI: [TEST] Connection FAILED!");
        }

        private async void RunAutoTest()
        {
            await Task.Delay(500); // Let the scene finish loading
            if (_isTestHost)
            {
                _isHosting = true;
            }
            else if (_isTestJoin)
            {
                _pendingRoomCode = "9999";
            }
            _networkManager?.ConnectToServer("127.0.0.1", 8080);
        }

        private async void AutoAddBotsAndStart()
        {
            // Wait for other human clients to join (up to 15s)
            GD.Print("LobbyUI: [TEST] Waiting for joiners...");
            for (int wait = 0; wait < 30; wait++) // 30 x 500ms = 15s
            {
                await Task.Delay(500);
                // Check server state via board sync count
                if (_networkManager == null) return;
            }

            // Fill remaining seats with bots (up to 4 players)
            int botsNeeded = 4 - 1; // We don't know exact count, so fill to 4
            // Actually, let the server reject extras. Just try adding 3.
            for (int i = 0; i < 3; i++)
            {
                GD.Print($"LobbyUI: [TEST] Adding bot {i + 1}...");
                _networkManager?.RpcId(1, "RequestAddBot");
                await Task.Delay(500);
            }
            await Task.Delay(1000);
            GD.Print("LobbyUI: [TEST] Starting game...");
            _networkManager?.RpcId(1, "RequestStartGame");
        }

        // ================================================================
        // SOCIAL TAB SWITCHING
        // ================================================================

        private void SwitchTab(string tab)
        {
            if (FriendsScrollContainer != null) FriendsScrollContainer.Visible = (tab == "friends");
            if (RequestsScrollContainer != null) RequestsScrollContainer.Visible = (tab == "requests");

            if (tab == "requests") LoadPendingRequests();
        }

        private async void LoadPendingRequests()
        {
            if (RequestsListContainer == null) return;

            var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
            if (accountMgr?.Supabase == null || !accountMgr.Supabase.IsAuthenticated) return;

            foreach (Node child in RequestsListContainer.GetChildren())
                child.QueueFree();

            var friendService = new Core.Networking.FriendService(accountMgr.Supabase);
            var requests = await friendService.GetPendingRequests();

            if (requests.Count == 0)
            {
                var emptyLabel = new Label {
                    Text = "No pending requests",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    CustomMinimumSize = new Vector2(0, 40)
                };
                emptyLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
                RequestsListContainer.AddChild(emptyLabel);
                return;
            }

            foreach (var req in requests)
            {
                var entry = new HBoxContainer();
                entry.CustomMinimumSize = new Vector2(0, 44);
                entry.AddThemeConstantOverride("separation", 6);

                var nameLabel = new Label {
                    Text = req.RequesterName,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill
                };
                entry.AddChild(nameLabel);

                var acceptBtn = new Button { Text = "✓", CustomMinimumSize = new Vector2(36, 0) };
                var declineBtn = new Button { Text = "✕", CustomMinimumSize = new Vector2(36, 0) };

                var reqId = req.FriendshipId;
                acceptBtn.Pressed += async () => {
                    bool ok = await friendService.AcceptFriendRequest(reqId);
                    if (ok) { StatusLabel.Text = $"Accepted {req.RequesterName}!"; LoadPendingRequests(); LoadFriends(); }
                };
                declineBtn.Pressed += async () => {
                    bool ok = await friendService.DeclineFriendRequest(reqId);
                    if (ok) { StatusLabel.Text = $"Declined request"; LoadPendingRequests(); }
                };

                entry.AddChild(acceptBtn);
                entry.AddChild(declineBtn);
                RequestsListContainer.AddChild(entry);
            }
        }

        // ================================================================
        // INVITE FRIEND TO ROOM
        // ================================================================

        private void InviteFriendToRoom(string friendId, string friendName)
        {
            if (!_isInRoom || _networkManager == null)
            {
                StatusLabel.Text = "Create or join a room first";
                return;
            }
            // Send invite via server RPC (server relays to target player)
            _networkManager.RpcId(1, "RelayInvite", friendId);
            StatusLabel.Text = $"Invited {friendName}!";
            GD.Print($"LobbyUI: Invited {friendName} ({friendId}) to room");
        }

        // ================================================================
        // PRIVACY SETTINGS
        // ================================================================

        private async void OnSettingsPressed()
        {
            var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
            if (accountMgr?.Supabase == null || !accountMgr.Supabase.IsAuthenticated)
            {
                StatusLabel.Text = "Sign in to access settings";
                return;
            }

            var presenceService = new Core.Networking.PresenceService(accountMgr.Supabase);
            var settings = await presenceService.GetPrivacySettings();

            // Build a popup settings panel
            var popup = new AcceptDialog();
            popup.Title = "Privacy Settings";
            popup.Size = new Vector2I(400, 360);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 12);

            // Online status visibility
            var statusLabel = new Label { Text = "Who can see your online status:" };
            var statusOption = new OptionButton();
            statusOption.AddItem("Everyone", 0);
            statusOption.AddItem("Friends Only", 1);
            statusOption.AddItem("Nobody", 2);
            statusOption.Selected = settings.OnlineStatusVisibility switch {
                "everyone" => 0, "friends" => 1, "nobody" => 2, _ => 1
            };
            vbox.AddChild(statusLabel);
            vbox.AddChild(statusOption);

            // Allow friend requests
            var reqLabel = new Label { Text = "Allow friend requests from:" };
            var reqOption = new OptionButton();
            reqOption.AddItem("Everyone", 0);
            reqOption.AddItem("Friends of Friends", 1);
            reqOption.AddItem("Nobody", 2);
            reqOption.Selected = settings.AllowFriendRequests switch {
                "everyone" => 0, "fof" => 1, "nobody" => 2, _ => 0
            };
            vbox.AddChild(reqLabel);
            vbox.AddChild(reqOption);

            // Show last seen
            var lastSeenCheck = new CheckBox { Text = "Show last seen", ButtonPressed = settings.ShowLastSeen };
            vbox.AddChild(lastSeenCheck);

            // Profile visibility
            var profileCheck = new CheckBox {
                Text = "Public profile (visible to non-friends)",
                ButtonPressed = settings.ProfileVisibility == "public"
            };
            vbox.AddChild(profileCheck);

            // Show in leaderboards
            var leaderboardCheck = new CheckBox { Text = "Show in leaderboards", ButtonPressed = settings.ShowInLeaderboards };
            vbox.AddChild(leaderboardCheck);

            popup.AddChild(vbox);

            popup.Confirmed += async () => {
                var updated = new Core.Networking.PrivacySettingsData {
                    OnlineStatusVisibility = statusOption.Selected switch { 0 => "everyone", 1 => "friends", _ => "nobody" },
                    AllowFriendRequests = reqOption.Selected switch { 0 => "everyone", 1 => "fof", _ => "nobody" },
                    ShowLastSeen = lastSeenCheck.ButtonPressed,
                    ProfileVisibility = profileCheck.ButtonPressed ? "public" : "friends",
                    ShowInLeaderboards = leaderboardCheck.ButtonPressed
                };
                bool ok = await presenceService.UpdatePrivacySettings(updated);
                StatusLabel.Text = ok ? "Settings saved!" : "Failed to save settings";
            };

            AddChild(popup);
            popup.PopupCentered();
        }
    }
}
