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
        private Core.Networking.PresenceService _presenceService;
        private AudioEngine _audioEngine;
#pragma warning disable CS0169 // The field is never used
        private Label _requestsBadge;
#pragma warning restore CS0169
        private HashSet<string> _selectedFriendIds = new HashSet<string>();
        private List<string> _pendingInviteIds = new List<string>();

        private bool _isHosting = false;
        private bool _isBrowsing = false;
        private string _pendingRoomCode = "";
        private string _currentRoomCode = "";
        private bool _isInRoom = false;
        private bool _isTestHost = false;
        private bool _isTestJoin = false;

        public override void _Ready()
        {
            _audioEngine = GetNodeOrNull<AudioEngine>("/root/AudioEngine");
            // ApplyGlassmorphism removed from here
            
            StatusLabel = GetNode<Label>("MarginContainer/MainLayout/RoomManagement/VBoxContainer/StatusLabel");
            RoomCodeInput = GetNode<LineEdit>("MarginContainer/MainLayout/RoomManagement/VBoxContainer/PrivateSection/HBoxContainer/RoomCodeInput");
            JoinButton = GetNode<Button>("MarginContainer/MainLayout/RoomManagement/VBoxContainer/PrivateSection/HBoxContainer/JoinButton");
            HostButton = GetNode<Button>("MarginContainer/MainLayout/RoomManagement/VBoxContainer/PrivateSection/HostButton");
            BrowseButton = GetNode<Button>("MarginContainer/MainLayout/RoomManagement/VBoxContainer/PublicSection/BrowseButton");
            SetupFriendUI();

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

            // Add a prominent Sign Out button to the UI
            AddSignOutButton();

            var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
            if (accountMgr != null)
            {
                // Start presence heartbeat ONCE during module startup
                if (accountMgr.Supabase != null && accountMgr.Supabase.IsAuthenticated)
                {
                    _presenceService = new Core.Networking.PresenceService(accountMgr.Supabase);
                    _presenceService.Start();
                }

                accountMgr.SignedIn += (uid, name) => UpdateWelcomeMessage(name);
                accountMgr.ProfileLoaded += (name, url, lv, gp, gw) => {
                    UpdateWelcomeMessage(name);
                    LoadFriends(); // Refresh friends list once we know we are fully authed
                };

                // Initial display if already loaded
                if (!string.IsNullOrEmpty(accountMgr.DisplayName))
                    UpdateWelcomeMessage(accountMgr.DisplayName);

                // Realtime Sync
                if (accountMgr.Realtime != null)
                {
                    accountMgr.Realtime.MessageReceived += OnRealtimeMessage;
                }

                accountMgr.RejoinGameAvailable += OnRejoinGameAvailable;
            }

            LoadFriends();
            CreateRequestsBadge();
            UpdateRequestsBadge();

            ApplyGlassmorphism(); // Style everything at the very end

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
            style.BgColor = new Color(0.12f, 0.16f, 0.15f, 0.95f); // Premium dark teal/grey
            style.CornerRadiusBottomLeft = 24;
            style.CornerRadiusBottomRight = 24;
            style.CornerRadiusTopLeft = 24;
            style.CornerRadiusTopRight = 24;
            style.BorderWidthBottom = 1; // Cleaner 1px border
            style.BorderWidthLeft = 1;
            style.BorderWidthRight = 1;
            style.BorderWidthTop = 1;
            style.BorderColor = new Color(0.72f, 0.53f, 0.04f, 0.4f); // Golden border
            style.ShadowColor = new Color(0, 0, 0, 0.4f);
            style.ShadowSize = 20;

            var roomPanel = GetNodeOrNull<PanelContainer>("MarginContainer/MainLayout/RoomManagement");
            roomPanel?.AddThemeStyleboxOverride("panel", style);

            // Try the new hierarchy first, fallback to old for safety
            var friendsPanel = GetNodeOrNull<PanelContainer>("MarginContainer/MainLayout/SocialColumn/FriendsPanel")
                               ?? GetNodeOrNull<PanelContainer>("MarginContainer/MainLayout/FriendsPanel");
            friendsPanel?.AddThemeStyleboxOverride("panel", style);

            // Apply global styling to all buttons and inputs
            StylePremiumButton(HostButton);
            StylePremiumButton(JoinButton);
            StylePremiumButton(BrowseButton);
            StylePremiumButton(OfflineButton);
            StylePremiumButton(AddFriendButton);
            StylePremiumButton(FriendsTab);
            StylePremiumButton(RequestsTab);
            StylePremiumButton(SettingsButton);

            StylePremiumInput(RoomCodeInput);
            StylePremiumInput(AddFriendInput);

            // FORCE Search Button visibility and icon
            var searchBtn = GetNodeOrNull<Button>("MarginContainer/MainLayout/SocialColumn/FriendsPanel/VBoxContainer/AddFriendBox/AddButton")
                           ?? GetNodeOrNull<Button>("MarginContainer/MainLayout/FriendsPanel/VBoxContainer/AddFriendBox/AddButton");
            
            var friendBox = GetNodeOrNull<HBoxContainer>("MarginContainer/MainLayout/SocialColumn/FriendsPanel/VBoxContainer/AddFriendBox")
                            ?? GetNodeOrNull<HBoxContainer>("MarginContainer/MainLayout/FriendsPanel/VBoxContainer/AddFriendBox");
            
            if (friendBox != null)
            {
                friendBox.AddThemeConstantOverride("separation", 8);
            }

            if (searchBtn != null)
            {
                searchBtn.Visible = true;
                searchBtn.Text = ""; 
                searchBtn.Icon = null; // Clear to avoid double icons
                searchBtn.CustomMinimumSize = new Vector2(44, 40);
                
                var icon = GD.Load<Texture2D>("res://Assets/search_icon.png");
                if (icon != null)
                {
                    // Nuclear overlay - fully centered with margin
                    foreach (var child in searchBtn.GetChildren()) if (child is TextureRect) child.QueueFree();
                    var tr = new TextureRect {
                        Texture = icon,
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        Name = "IconOverlay",
                        MouseFilter = Control.MouseFilterEnum.Pass
                    };
                    searchBtn.AddChild(tr);
                    tr.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                    tr.OffsetLeft = 10;
                    tr.OffsetRight = -10;
                    tr.OffsetTop = 8;
                    tr.OffsetBottom = -8;
                }

                var searchStyle = new StyleBoxFlat {
                    BgColor = new Color(0.18f, 0.22f, 0.21f, 1.0f), // Premium Dark
                    BorderColor = new Color(0.72f, 0.53f, 0.04f, 0.6f),
                    BorderWidthBottom = 1,
                    BorderWidthLeft = 1,
                    BorderWidthRight = 1,
                    BorderWidthTop = 1,
                    CornerRadiusTopLeft = 8,
                    CornerRadiusTopRight = 8,
                    CornerRadiusBottomLeft = 8,
                    CornerRadiusBottomRight = 8
                };
                searchBtn.AddThemeStyleboxOverride("normal", searchStyle);
                searchBtn.AddThemeStyleboxOverride("hover", searchStyle);
                searchBtn.AddThemeStyleboxOverride("pressed", searchStyle);
            }
        }

        private void StylePremiumButton(Button btn)
        {
            if (btn == null) return;

            var normal = new StyleBoxFlat {
                BgColor = new Color(1, 1, 1, 0.05f),
                BorderColor = new Color(1, 1, 1, 0.15f),
                BorderWidthBottom = 1,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                CornerRadiusTopLeft = 8,
                CornerRadiusTopRight = 8,
                CornerRadiusBottomLeft = 8,
                CornerRadiusBottomRight = 8,
                AntiAliasing = true
            };

            var hover = normal.Duplicate() as StyleBoxFlat;
            hover.BorderColor = new Color(0.72f, 0.53f, 0.04f, 0.6f); // Golden highlight
            hover.BgColor = new Color(1, 1, 1, 0.1f);

            var pressed = normal.Duplicate() as StyleBoxFlat;
            pressed.BgColor = new Color(0.12f, 0.16f, 0.15f, 0.3f);
            pressed.BorderColor = new Color(0.72f, 0.53f, 0.04f, 0.8f);

            btn.AddThemeStyleboxOverride("normal", normal);
            btn.AddThemeStyleboxOverride("hover", hover);
            btn.AddThemeStyleboxOverride("pressed", pressed);
            btn.AddThemeStyleboxOverride("focus", hover);
            btn.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        }

        private void StylePremiumInput(LineEdit input)
        {
            if (input == null) return;

            var style = new StyleBoxFlat {
                BgColor = new Color(0, 0, 0, 0.3f),
                BorderColor = new Color(1, 1, 1, 0.1f),
                BorderWidthBottom = 1,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                CornerRadiusTopLeft = 8,
                CornerRadiusTopRight = 8,
                CornerRadiusBottomLeft = 8,
                CornerRadiusBottomRight = 8,
                AntiAliasing = true
            };

            input.AddThemeStyleboxOverride("normal", style);
            input.AddThemeStyleboxOverride("focus", style);
            input.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.9f));
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

            if (_networkManager?.IsActive == true)
            {
                // Already connected (from auto-registration) — create room directly
                StatusLabel.Text = "Creating room...";
                var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
                _networkManager.CreateRoomOnServer(accountMgr?.DisplayName ?? "", accountMgr?.AvatarUrl ?? "");
            }
            else
            {
                StatusLabel.Text = "Connecting to server...";
                _networkManager?.ConnectToServer("127.0.0.1", 8080);
            }
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

            if (_networkManager?.IsActive == true)
            {
                // Already connected — join directly
                StatusLabel.Text = $"Joining room {_pendingRoomCode}...";
                var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
                _networkManager.JoinRoomOnServer(_pendingRoomCode, "", accountMgr?.DisplayName ?? "", accountMgr?.AvatarUrl ?? "");
            }
            else
            {
                StatusLabel.Text = $"Joining room {_pendingRoomCode}...";
                _networkManager?.ConnectToServer("127.0.0.1", 8080);
            }
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
                var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
                string name = accountMgr?.DisplayName ?? "";
                string avatar = accountMgr?.AvatarUrl ?? "";
                _networkManager?.CreateRoomOnServer(name, avatar);
            }
            else
            {
                StatusLabel.Text = "Joining room...";
                var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
                string name = accountMgr?.DisplayName ?? "";
                string avatar = accountMgr?.AvatarUrl ?? "";
                _networkManager?.JoinRoomOnServer(_pendingRoomCode, "", name, avatar);
            }
        }

        private void OnGameInviteReceived(string inviterName, string roomCode, string inviterId)
        {
            GD.Print($"LobbyUI: Invite received from {inviterName} ({inviterId}) for room {roomCode}");

            // Clear the invite from Supabase record so it doesn't pop up again immediately
            var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
            if (accountMgr?.Supabase != null && accountMgr.Supabase.IsAuthenticated)
            {
                _ = accountMgr.Supabase.Rpc("clear_invite");
            }

            var popup = new ConfirmationDialog();
            popup.Title = "Game Invite";
            popup.DialogText = $"{inviterName} invited you to play!\nRoom: {roomCode}";
            popup.OkButtonText = "Accept ✓";
            popup.CancelButtonText = "Decline ✗";
            popup.Size = new Vector2I(340, 140);

            AddChild(popup);
            popup.PopupCentered();

            popup.Confirmed += () => {
                StatusLabel.Text = $"Joining {inviterName}'s room...";
                _isHosting = false;
                _pendingRoomCode = roomCode;
                _networkManager?.ConnectToServer("127.0.0.1", 8080);
                popup.QueueFree();
            };
            popup.Canceled += () => {
                GD.Print($"LobbyUI: Declining invite from {inviterId}");
                if (accountMgr?.Supabase != null)
                {
                    _ = accountMgr.Supabase.Rpc("decline_invite", new { p_inviter_id = inviterId });
                }
                popup.QueueFree();
            };
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
            var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
            string name = accountMgr?.DisplayName ?? "";
            string avatar = accountMgr?.AvatarUrl ?? "";
            _networkManager?.JoinRoomOnServer(code, "", name, avatar);
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
                ShowSearchResultsPopup(results);
            }
            else
            {
                StatusLabel.Text = "Player not found";
            }
            AddFriendInput.Text = "";
        }

        private void ShowSearchResultsPopup(List<Core.Networking.PlayerSearchResult> results)
        {
            var popup = new AcceptDialog();
            popup.Title = "Search Results";
            popup.Size = new Vector2I(450, 400);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_top", 10);
            margin.AddThemeConstantOverride("margin_left", 10);
            margin.AddThemeConstantOverride("margin_right", 10);
            margin.AddThemeConstantOverride("margin_bottom", 10);
            
            var scroll = new ScrollContainer();
            scroll.CustomMinimumSize = new Vector2(400, 300);
            
            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 10);
            vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            
            scroll.AddChild(vbox);
            margin.AddChild(scroll);
            popup.AddChild(margin);
            
            AddChild(popup);
            popup.PopupCentered();

            var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
            var friendService = new Core.Networking.FriendService(accountMgr.Supabase);

            foreach (var player in results)
            {
                var hbox = new HBoxContainer();
                hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                
                var nameLabel = new Label { 
                    Text = $"{player.DisplayName} (Lv.{player.Level})",
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    VerticalAlignment = VerticalAlignment.Center
                };
                
                var addBtn = new Button { 
                    Text = "Add Friend",
                    CustomMinimumSize = new Vector2(100, 35)
                };
                
                addBtn.Pressed += async () => {
                    bool sent = await friendService.SendFriendRequest(player.PlayerId);
                    StatusLabel.Text = sent ? $"Friend request sent to {player.DisplayName}" : "Failed to send request";
                    popup.Hide();
                    popup.QueueFree();
                };
                
                hbox.AddChild(nameLabel);
                hbox.AddChild(addBtn);
                vbox.AddChild(hbox);
            }
            
            popup.Confirmed += () => popup.QueueFree();
            popup.Canceled += () => popup.QueueFree();
        }

        private async void LoadFriends()
        {
            var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
            if (accountMgr?.Supabase == null || !accountMgr.Supabase.IsAuthenticated)
            {
                // Not signed in — show empty or local placeholder
                return;
            }

            var friendService = new Core.Networking.FriendService(accountMgr.Supabase);
            var friends = await friendService.GetFriends();
            RefreshFriendsList(friends);
        }

        private void RefreshFriendsList(List<Core.Networking.FriendInfo> friends = null)
        {
            if (FriendsListContainer == null) return;
            var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");

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
                var friendId = friend.PlayerId;
                var friendName = friend.DisplayName;

                // Inform us if they declined an invite
                if (friend.InviteResponse == "declined" && friend.InviteFromId == accountMgr?.Supabase?.UserId)
                {
                    StatusLabel.Text = $"{friendName} declined your invitation.";
                }

                var entry = new HBoxContainer();
                entry.CustomMinimumSize = new Vector2(0, 40);
                entry.AddThemeConstantOverride("separation", 12); // Slightly more space

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

                // Custom Checkbox (using a Button for better styling control)
                bool canInvite = friend.OnlineStatus == "online" || friend.OnlineStatus == "away" || string.IsNullOrEmpty(friend.OnlineStatus);
                
                var cb = new Button {
                    ToggleMode = true,
                    ButtonPressed = _selectedFriendIds.Contains(friendId),
                    CustomMinimumSize = new Vector2(24, 24),
                    SizeFlagsVertical = SizeFlags.ShrinkCenter,
                    Disabled = !canInvite,
                    TooltipText = canInvite ? "Select for multi-invite" : "Friend is currently offline or in-game",
                    Text = _selectedFriendIds.Contains(friendId) ? "✓" : ""
                };

                // Premium square styling
                var cbNormal = new StyleBoxFlat {
                    BgColor = new Color(0, 0, 0, 0.4f), // Darker box background
                    BorderColor = new Color(1, 1, 1, 0.4f), // Clearer white border
                    BorderWidthBottom = 1,
                    BorderWidthLeft = 1,
                    BorderWidthRight = 1,
                    BorderWidthTop = 1,
                    CornerRadiusTopLeft = 4,
                    CornerRadiusTopRight = 4,
                    CornerRadiusBottomLeft = 4,
                    CornerRadiusBottomRight = 4,
                    AntiAliasing = true
                };

                var cbHover = cbNormal.Duplicate() as StyleBoxFlat;
                cbHover.BorderColor = new Color(1, 1, 1, 0.9f);
                cbHover.BgColor = new Color(1, 1, 1, 0.2f);

                var cbPressed = cbNormal.Duplicate() as StyleBoxFlat;
                cbPressed.BgColor = new Color(0.13f, 0.77f, 0.37f, 0.4f); // Brighter green when selected
                cbPressed.BorderColor = new Color(0.13f, 0.77f, 0.37f, 1.0f); // Solid green border

                var cbDisabled = cbNormal.Duplicate() as StyleBoxFlat;
                cbDisabled.BorderColor = new Color(1, 1, 1, 0.1f);
                cbDisabled.BgColor = new Color(0, 0, 0, 0.2f);

                cb.AddThemeStyleboxOverride("normal", cbNormal);
                cb.AddThemeStyleboxOverride("hover", cbHover);
                cb.AddThemeStyleboxOverride("pressed", cbPressed);
                cb.AddThemeStyleboxOverride("disabled", cbDisabled);
                cb.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.9f));
                cb.AddThemeFontSizeOverride("font_size", 14);

                cb.Toggled += (on) => {
                    cb.Text = on ? "✓" : "";
                    if (on) _selectedFriendIds.Add(friendId);
                    else _selectedFriendIds.Remove(friendId);
                };
                entry.AddChild(cb);

                // Name
                Label nameLabel = new Label {
                    Text = friend.DisplayName,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill
                };
                entry.AddChild(nameLabel);

                // Invite button (Single)
                Button inviteBtn = new Button {
                    Text = "INVITE",
                    CustomMinimumSize = new Vector2(60, 0)
                };
                inviteBtn.ThemeTypeVariation = "ButtonSmall";
                inviteBtn.Pressed += () => InviteFriendToRoom(friendId, friendName);
                entry.AddChild(inviteBtn);

                FriendsListContainer.AddChild(entry);
            }
        }

        private async Task FlushPendingInvites()
        {
            GD.Print($"LobbyUI: FlushPendingInvites called. Pending invites count: {_pendingInviteIds.Count}, Current Room Code: '{_currentRoomCode}'");
            if (_pendingInviteIds.Count > 0 && !string.IsNullOrEmpty(_currentRoomCode))
            {
                var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
                string myName = accountMgr?.DisplayName ?? "A friend";
                var supabase = accountMgr?.Supabase;

                foreach (var id in _pendingInviteIds)
                {
                    if (supabase != null)
                    {
                        GD.Print($"LobbyUI: [EXTREME DEBUG] Executing RPC send_invite to {id} for room {_currentRoomCode}");
                        await supabase.Rpc("send_invite", new {
                            p_target_player_id = id,
                            p_room_code = _currentRoomCode,
                            p_inviter_name = myName
                        });
                        GD.Print($"LobbyUI: [EXTREME DEBUG] RPC send_invite dispatched.");
                    }
                    else
                    {
                        GD.PrintErr("LobbyUI: Cannot flush invite, Supabase client is null");
                    }
                }
                StatusLabel.Text = $"Room ready! Invited {_pendingInviteIds.Count} friend(s).";
                _pendingInviteIds.Clear();
            }
        }

        private async void OnRoomCreated(string code)
        {
            _isInRoom = true;
            _currentRoomCode = code;
            StatusLabel.Text = $"Room Created: {code}";
            UpdateRoomUIForWaitingState();
            await FlushPendingInvites();
        }

        private async void OnRoomJoined(string code, int playerIndex)
        {
            GD.Print($"LobbyUI: [EXTREME DEBUG] OnRoomJoined invoked for room {code} as player {playerIndex}.");
            _isInRoom = true;
            _currentRoomCode = code;
            StatusLabel.Text = $"Joined Room: {code}";
            UpdateRoomUIForWaitingState();
            await FlushPendingInvites();

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
                if (_requestsBadge != null) _requestsBadge.Visible = false;
                var emptyLabel = new Label {
                    Text = "No pending requests",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    CustomMinimumSize = new Vector2(0, 40)
                };
                emptyLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
                RequestsListContainer.AddChild(emptyLabel);
                return;
            }

            if (_requestsBadge != null) _requestsBadge.Visible = true;

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
                    if (ok) { 
                        StatusLabel.Text = $"Accepted {req.RequesterName}!"; 
                        LoadPendingRequests(); 
                        LoadFriends(); 
                        UpdateRequestsBadge();
                    }
                };
                declineBtn.Pressed += async () => {
                    bool ok = await friendService.DeclineFriendRequest(reqId);
                    if (ok) { 
                        StatusLabel.Text = $"Declined request"; 
                        LoadPendingRequests(); 
                        UpdateRequestsBadge();
                    }
                };

                entry.AddChild(acceptBtn);
                entry.AddChild(declineBtn);
                RequestsListContainer.AddChild(entry);
            }
        }

        private void CreateRequestsBadge()
        {
            if (RequestsTab == null) return;

            _requestsBadge = new Label {
                Name = "RequestsBadge",
                Text = "!",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visible = false,
                CustomMinimumSize = new Vector2(18, 18)
            };

            var style = new StyleBoxFlat {
                BgColor = new Color(0.9f, 0.2f, 0.2f),
                CornerRadiusTopLeft = 9,
                CornerRadiusTopRight = 9,
                CornerRadiusBottomLeft = 9,
                CornerRadiusBottomRight = 9,
                AntiAliasing = true
            };
            _requestsBadge.AddThemeStyleboxOverride("normal", style);
            _requestsBadge.AddThemeFontSizeOverride("font_size", 12);
            _requestsBadge.AddThemeColorOverride("font_color", new Color(1, 1, 1));

            RequestsTab.AddChild(_requestsBadge);
            _requestsBadge.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
            _requestsBadge.Position = new Vector2(RequestsTab.Size.X - 22, 4);
        }

        private async void UpdateRequestsBadge()
        {
            var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
            if (accountMgr?.Supabase == null || !accountMgr.Supabase.IsAuthenticated) return;

            var friendService = new Core.Networking.FriendService(accountMgr.Supabase);
            var requests = await friendService.GetPendingRequests();
            
            if (_requestsBadge != null)
            {
                _requestsBadge.Visible = requests.Count > 0;
                // Re-position in case tab resized
                _requestsBadge.Position = new Vector2(RequestsTab.Size.X - 22, 4);
            }
        }

        // ================================================================
        // INVITE FRIEND TO ROOM
        // ================================================================

        private async void InviteFriendToRoom(string friendId, string friendName)
        {
            GD.Print($"LobbyUI: InviteFriendToRoom called for {friendName} ({friendId}). isInRoom: {_isInRoom}, currentCode: {_currentRoomCode}");
            
            if (_isInRoom && !string.IsNullOrEmpty(_currentRoomCode))
            {
                // Already in room — send via Supabase Realtime
                var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
                string myName = accountMgr?.DisplayName ?? "A friend";
                var supabase = accountMgr?.Supabase;
                
                if (supabase != null)
                {
                    GD.Print($"LobbyUI: Sending inline invite via RPC to {friendId}");
                    await supabase.Rpc("send_invite", new {
                        p_target_player_id = friendId,
                        p_room_code = _currentRoomCode,
                        p_inviter_name = myName
                    });
                }
                else
                {
                    GD.PrintErr("LobbyUI: Supabase null in InviteFriendToRoom, cannot send.");
                }

                StatusLabel.Text = $"Invited {friendName}!";
                GD.Print($"LobbyUI: Invited {friendName} ({friendId}) to room");
            }
            else
            {
                // Auto-create room, then send invite once it's ready
                _pendingInviteIds.Add(friendId);
                StatusLabel.Text = $"Creating room to invite {friendName}...";
                GD.Print($"LobbyUI: Auto-hosting to invite {friendName} ({friendId})");
                OnHostPressed();
            }
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

            // Spacer
            vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

            // Logout Button
            var logoutBtn = new Button { 
                Text = "LOGOUT", 
                CustomMinimumSize = new Vector2(0, 45),
                SelfModulate = new Color(1, 0.3f, 0.3f) // reddish
            };
            logoutBtn.Pressed += () => {
                popup.Hide();
                accountMgr.SignOut();
            };
            vbox.AddChild(logoutBtn);

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

        private void AddSignOutButton()
        {
            var signOutBtn = new Button {
                Name = "SignOutButton",
                Text = "SIGN OUT",
                CustomMinimumSize = new Vector2(0, 44),
            };

            // Hierarchy Adjustment: Wrap FriendsPanel in a VBox to place button below it
            var friendsPanelPath = "MarginContainer/MainLayout/FriendsPanel";
            var friendsPanel = GetNodeOrNull<PanelContainer>(friendsPanelPath);
            
            if (friendsPanel != null)
            {
                var mainLayout = friendsPanel.GetParent() as Control;
                if (mainLayout != null)
                {
                    int index = friendsPanel.GetIndex();
                    mainLayout.RemoveChild(friendsPanel);
                    
                    var column = new VBoxContainer {
                        Name = "SocialColumn",
                        SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                        CustomMinimumSize = new Vector2(300, 0) // Maintain the social panel width
                    };
                    column.AddThemeConstantOverride("separation", 12);
                    
                    mainLayout.AddChild(column);
                    mainLayout.MoveChild(column, index);
                    
                    column.AddChild(friendsPanel);
                    friendsPanel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                    
                    column.AddChild(signOutBtn);
                }
            }
            else
            {
                // Fallback to top-right if structure is missing
                AddChild(signOutBtn);
                signOutBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
                signOutBtn.OffsetLeft = -120;
                signOutBtn.OffsetTop = 20;
                signOutBtn.OffsetRight = -20;
                signOutBtn.OffsetBottom = 64;
            }

            // Premium Styling - Red accented glass
            var style = new StyleBoxFlat {
                BgColor = new Color(0.8f, 0.2f, 0.2f, 0.15f),
                BorderColor = new Color(0.8f, 0.2f, 0.2f, 0.6f),
                BorderWidthBottom = 1,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                CornerRadiusTopLeft = 8,
                CornerRadiusTopRight = 8,
                CornerRadiusBottomLeft = 8,
                CornerRadiusBottomRight = 8
            };
            
            var hoverStyle = style.Duplicate() as StyleBoxFlat;
            hoverStyle.BgColor = new Color(0.8f, 0.2f, 0.2f, 0.25f);
            hoverStyle.BorderColor = new Color(1.0f, 0.3f, 0.3f, 0.8f);

            signOutBtn.AddThemeStyleboxOverride("normal", style);
            signOutBtn.AddThemeStyleboxOverride("hover", hoverStyle);
            signOutBtn.AddThemeStyleboxOverride("pressed", hoverStyle);
            signOutBtn.AddThemeFontSizeOverride("font_size", 14);
            signOutBtn.AddThemeColorOverride("font_color", new Color(1, 0.95f, 0.95f));
            
            signOutBtn.Pressed += () => {
                var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
                accountMgr?.SignOut();
            };
        }

        private void UpdateWelcomeMessage(string name)
        {
            if (StatusLabel != null && !string.IsNullOrEmpty(name))
            {
                StatusLabel.Text = $"Welcome back, {name}";
            }
        }
        private void OnRealtimeMessage(string topic, string @event, string payload)
        {
            if (topic.Contains("friendships") || topic.Contains("player_presence"))
            {
                GD.Print($"LobbyUI: Realtime update received on {topic}. Payload: {payload}");

                // Parse payload to check for invites in player_presence updates
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(payload);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("data", out var dataNode))
                    {
                        root = dataNode;
                    }

                    if (root.TryGetProperty("record", out var record))
                    {
                        var accountMgr = GetNodeOrNull<Core.Networking.AccountManager>("/root/AccountManager");
                        if (accountMgr?.Supabase?.UserId != null &&
                            record.TryGetProperty("player_id", out var pId) && 
                            pId.GetString() == accountMgr.Supabase.UserId)
                        {
                            if (record.TryGetProperty("invite_room_code", out var invCode) && invCode.ValueKind != System.Text.Json.JsonValueKind.Null && 
                                record.TryGetProperty("invite_from_name", out var invName) && invName.ValueKind != System.Text.Json.JsonValueKind.Null &&
                                record.TryGetProperty("invite_from_id", out var invId) && invId.ValueKind != System.Text.Json.JsonValueKind.Null)
                            {
                                string roomCode = invCode.GetString();
                                string inviterName = invName.GetString();
                                string inviterId = invId.GetString();
                                if (!string.IsNullOrEmpty(roomCode))
                                {
                                    CallDeferred(nameof(OnGameInviteReceived), inviterName, roomCode, inviterId);
                                }
                            }
                        }
                    }
                }
                catch (System.Exception e)
                {
                    GD.Print($"LobbyUI: Error parsing realtime payload: {e.Message}");
                }

                GD.Print($"LobbyUI: Refreshing friends...");
                LoadFriends(); // Simple refresh for now
                UpdateRequestsBadge();
                if (RequestsScrollContainer != null && RequestsScrollContainer.Visible)
                    LoadPendingRequests();
            }
        }

        private void OnRejoinGameAvailable(string roomCode)
        {
            GD.Print($"LobbyUI: Reconnection available for room {roomCode}");
            
            // For now, let's just automate it or show a status message with a button
            StatusLabel.Text = $"Game in progress: {roomCode}. Click JOIN to resume.";
            RoomCodeInput.Text = roomCode;
            
            // Optionally: Show a dedicated popup here
        }

        private void SetupFriendUI()
        {
            // Redundant search button styling moved to ApplyGlassmorphism

            // 2. Refresh Button
            if (FriendsTab != null && FriendsTab.GetParent() is Control tabContainer)
            {
                var refreshBtn = new Button {
                    Name = "RefreshSocialButton",
                    Text = "Refresh",
                    CustomMinimumSize = new Vector2(80, 40),
                    TooltipText = "Manual sync friends and requests"
                };

                // Style it a bit if possible to match the glassmorphism or just the tabs
                tabContainer.AddChild(refreshBtn);
                StylePremiumButton(refreshBtn);
                // Move it to the end or before settings if it exists
                tabContainer.MoveChild(refreshBtn, tabContainer.GetChildCount() - 1);

                refreshBtn.Pressed += () => {
                    StatusLabel.Text = "Refreshing social status...";
                    LoadFriends();
                    UpdateRequestsBadge();
                    if (RequestsScrollContainer != null && RequestsScrollContainer.Visible)
                        LoadPendingRequests();
                };

                // 3. Invite Selected Button
                var multiInviteBtn = new Button {
                    Name = "MultiInviteButton",
                    Text = "Invite Selected",
                    CustomMinimumSize = new Vector2(100, 40),
                    TooltipText = "Invite all checked friends"
                };
                tabContainer.AddChild(multiInviteBtn);
                StylePremiumButton(multiInviteBtn);
                tabContainer.MoveChild(multiInviteBtn, tabContainer.GetChildCount() - 1);

                multiInviteBtn.Pressed += () => {
                    if (_selectedFriendIds.Count == 0)
                    {
                        StatusLabel.Text = "Select friends to invite first";
                        return;
                    }

                    if (_isInRoom)
                    {
                        // Already in room — send all immediately
                        int count = 0;
                        foreach (var id in _selectedFriendIds)
                        {
                            _networkManager?.RpcId(1, "RelayInvite", id);
                            count++;
                        }
                        StatusLabel.Text = $"Sent {count} invite(s)!";
                        _selectedFriendIds.Clear();
                        LoadFriends();
                    }
                    else
                    {
                        // Queue all selected IDs, auto-create room, flush in OnRoomCreated
                        foreach (var id in _selectedFriendIds)
                            _pendingInviteIds.Add(id);
                        StatusLabel.Text = $"Creating room for {_pendingInviteIds.Count} friend(s)...";
                        _selectedFriendIds.Clear();
                        LoadFriends();
                        OnHostPressed();
                    }
                };
            }
        }
    }
}
