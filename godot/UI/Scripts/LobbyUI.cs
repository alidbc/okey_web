using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
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

        private Core.Networking.NetworkManager _networkManager;
        private List<Friend> _friendsList = new List<Friend>();

        private bool _isHosting = false;
        private bool _isBrowsing = false;
        private string _pendingRoomCode = "";
        private bool _isInRoom = false;

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

            LoadFriends();
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

        private void OnAddFriendPressed()
        {
            string name = AddFriendInput.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var newFriend = new Friend(Guid.NewGuid().ToString().Substring(0, 8), name);
            _friendsList.Add(newFriend);
            AddFriendInput.Text = "";
            SaveFriends();
            RefreshFriendsList();
        }

        private void LoadFriends()
        {
            _friendsList = PersistenceManager.LoadFriends();
            RefreshFriendsList();
        }

        private void SaveFriends()
        {
            PersistenceManager.SaveFriends(_friendsList);
        }

        private void RefreshFriendsList()
        {
            if (FriendsListContainer == null) return;

            foreach (Node child in FriendsListContainer.GetChildren())
                child.QueueFree();

            foreach (var friend in _friendsList)
            {
                HBoxContainer entry = new HBoxContainer();
                entry.CustomMinimumSize = new Vector2(0, 40);
                
                Label nameLabel = new Label { Text = friend.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                entry.AddChild(nameLabel);

                Button inviteBtn = new Button { Text = "INVITE", CustomMinimumSize = new Vector2(60, 0) };
                inviteBtn.ThemeTypeVariation = "ButtonSmall";
                inviteBtn.Pressed += () => GD.Print($"Inviting {friend.Name}..."); // Placeholder
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
        }
    }
}
