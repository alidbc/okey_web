using Godot;
using System;
using OkieRummyGodot.Server;

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
        [Export] public PackedScene RoomEntryPrefab;

        private Core.Networking.NetworkManager _networkManager;

        private bool _isHosting = false;
        private bool _isBrowsing = false;
        private string _pendingRoomCode = "";

        public override void _Ready()
        {
            // The NetworkManager is an Autoload in Phase 3
            _networkManager = GetNodeOrNull<Core.Networking.NetworkManager>("/root/NetworkManager");
            
            if (_networkManager != null)
            {
                _networkManager.ConnectionSuccessful += OnConnectionSuccessful;
                _networkManager.ConnectionFailed += OnConnectionFailed;
                _networkManager.RoomError += OnRoomError;
                _networkManager.RoomListReceived += OnRoomListReceived;
            }

            HostButton.Pressed += OnHostPressed;
            JoinButton.Pressed += OnJoinPressed;
            OfflineButton.Pressed += OnOfflinePressed;
            if (BrowseButton != null) BrowseButton.Pressed += OnBrowsePressed;
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

            // Clear existing
            foreach (Node child in RoomListContainer.GetChildren())
            {
                child.QueueFree();
            }

            if (data.Rooms.Count == 0)
            {
                StatusLabel.Text = "No public rooms found.";
                return;
            }

            StatusLabel.Text = $"Found {data.Rooms.Count} rooms.";

            foreach (var room in data.Rooms)
            {
                // For simplicity, we create a button for each room
                Button btn = new Button();
                btn.Text = $"Room {room.Code} ({room.PlayerCount}/4) - {room.Status}";
                btn.CustomMinimumSize = new Vector2(0, 40);
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
