using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using OkieRummyGodot.Core.Application;
using OkieRummyGodot.Core.Domain;

namespace OkieRummyGodot.Core.Networking
{
    public partial class NetworkManager : Node
    {
        private ENetMultiplayerPeer _peer = new ENetMultiplayerPeer();
        
        [Signal] public delegate void ConnectionSuccessfulEventHandler();
        [Signal] public delegate void ConnectionFailedEventHandler();
        [Signal] public delegate void ServerDisconnectedEventHandler();
        [Signal] public delegate void BoardStateSyncedEventHandler(string json);
        [Signal] public delegate void PrivateRackSyncedEventHandler(string json);
        [Signal] public delegate void RoomCreatedEventHandler(string code);
        [Signal] public delegate void RoomJoinedEventHandler(string code, int playerIndex);
        [Signal] public delegate void RoomErrorEventHandler(string message);
        [Signal] public delegate void RoomListReceivedEventHandler(string json);
        [Signal] public delegate void WinCheckResultReceivedEventHandler(bool success, string message);
        [Signal] public delegate void TileDrawnEventHandler(string playerId, bool fromDiscard, int targetSlotIndex, string tileId, int value, int color, bool wasDrag);
        [Signal] public delegate void TileDiscardedEventHandler(string playerId, int rackIndex, string tileId, int value, int color);

        [Export] public string MainGameScenePath = "res://UI/Scenes/Main.tscn";
        
        public int LocalPlayerIndex { get; private set; } = -1;
        private Dictionary<string, MatchManager> _rooms = new Dictionary<string, MatchManager>();
        private Dictionary<int, string> _peerToRoom = new Dictionary<int, string>();
        private Dictionary<int, int> _peerToPlayer = new Dictionary<int, int>(); // peerId -> playerIndex (0-3) within their room
        private Dictionary<string, (string roomId, int playerIndex)> _tokenToSession = new Dictionary<string, (string, int)>();
        private Dictionary<(string roomId, int playerIndex), int> _sessionToPeer = new Dictionary<(string, int), int>();
        private Dictionary<int, long> _lastRpcTime = new Dictionary<int, long>();
        private double _tickTimer = 0;

        public bool IsActive => Multiplayer.MultiplayerPeer != null && 
                                Multiplayer.MultiplayerPeer is ENetMultiplayerPeer &&
                                Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

        public bool IsHost()
        {
            if (Multiplayer.IsServer())
            {
                // On server, we can check the room data
                int senderId = Multiplayer.GetRemoteSenderId();
                if (!_peerToRoom.ContainsKey(senderId)) return false;
                string code = _peerToRoom[senderId];
                if (!_rooms.ContainsKey(code)) return false;
                return _peerToPlayer[senderId] == 0;
            }
            
            // On client, we rely on our assigned index
            return LocalPlayerIndex == 0;
        }

        public string GetRoomCode(int peerId) => _peerToRoom.ContainsKey(peerId) ? _peerToRoom[peerId] : "";

        public override void _Ready()
        {
            ForceReady();
        }

        public override void _Process(double delta)
        {
            // Diagnostic monitoring
            var currentStatus = Multiplayer.MultiplayerPeer?.GetConnectionStatus() ?? MultiplayerPeer.ConnectionStatus.Disconnected;
            if (currentStatus != _lastStatus)
            {
                GD.Print($"NetworkManager: ConnectionStatus changed from {_lastStatus} to {currentStatus}");
                _lastStatus = currentStatus;
            }

            if (!Multiplayer.IsServer()) return;

            _tickTimer += delta;
            if (_tickTimer >= 1.0)
            {
                _tickTimer = 0;
                // Use a copy of keys to avoid modification while iterating if CleanupRoom is called
                var roomIds = _rooms.Keys.ToList();
                foreach (var roomId in roomIds)
                {
                    if (_rooms.TryGetValue(roomId, out var match))
                    {
                        match.CheckTimeouts();
                    }
                }
            }
        }

        private void ForceReady()
        {
            GD.Print($"NetworkManager: Ready at path {GetPath()}");
            Multiplayer.ConnectedToServer += OnConnectedToServer;
            Multiplayer.ConnectionFailed += OnConnectionFailed;
            Multiplayer.ServerDisconnected += OnServerDisconnected;
            
            if (Multiplayer.IsServer())
            {
                GD.Print("NetworkManager: Server mode initialized.");
                Multiplayer.PeerDisconnected += OnPeerDisconnectedOnServer;
            }
        }

        private MultiplayerPeer.ConnectionStatus _lastStatus = MultiplayerPeer.ConnectionStatus.Disconnected;

        public void Disconnect()
        {
            GD.Print("NetworkManager: Force disconnecting and resetting state...");
            
            // Close connection if active
            if (Multiplayer.MultiplayerPeer != null && !(Multiplayer.MultiplayerPeer is OfflineMultiplayerPeer))
            {
                GD.Print($"NetworkManager: Closing existing peer of type {Multiplayer.MultiplayerPeer.GetType().Name}");
                
                // Disconnect signals to avoid overlapping callbacks or stale references
                try {
                    Multiplayer.ConnectedToServer -= OnConnectedToServer;
                    Multiplayer.ConnectionFailed -= OnConnectionFailed;
                    Multiplayer.ServerDisconnected -= OnServerDisconnected;
                } catch { /* Ignore if already disconnected */ }

                if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet)
                {
                    enet.Close();
                }
            }
            
            Multiplayer.MultiplayerPeer = null;
            _peer = null;

            // Reset local state
            LocalPlayerIndex = -1;
            _rooms.Clear();
            _peerToRoom.Clear();
            _peerToPlayer.Clear();
            _tokenToSession.Clear();
            _sessionToPeer.Clear();
            _lastRpcTime.Clear();
            _tickTimer = 0;
            _lastStatus = MultiplayerPeer.ConnectionStatus.Disconnected;
        }

        private void OnPeerDisconnectedOnServer(long id)
        {
            int peerId = (int)id;
            if (_peerToRoom.ContainsKey(peerId))
            {
                string roomId = _peerToRoom[peerId];
                int playerIndex = _peerToPlayer[peerId];
                
                var match = _rooms[roomId];
                match.Players[playerIndex].ConnectionState = PlayerConnectionState.TEMP_DISCONNECTED;
                
                _peerToRoom.Remove(peerId);
                _peerToPlayer.Remove(peerId);
                _lastRpcTime.Remove(peerId);
                _sessionToPeer.Remove((roomId, playerIndex));
                
                GD.Print($"NetworkManager: Peer {peerId} (Player {playerIndex}) disconnected from room {roomId}");
                
                // Check if any active peers remain in this room
                bool roomEmpty = true;
                foreach (var kvp in _peerToRoom)
                {
                    if (kvp.Value == roomId)
                    {
                        roomEmpty = false;
                        break;
                    }
                }

                if (roomEmpty)
                {
                    CleanupRoom(roomId);
                }
                else
                {
                    BroadcastGameStateInRoom(roomId);
                }
            }
        }

        private void CleanupRoom(string roomId)
        {
            if (!_rooms.ContainsKey(roomId)) return;

            GD.Print($"NetworkManager: Room {roomId} is empty. Cleaning up...");

            // 1. Remove tokens and sessions associated with this room
            var tokensToRemove = new List<string>();
            foreach (var kvp in _tokenToSession)
            {
                if (kvp.Value.roomId == roomId) tokensToRemove.Add(kvp.Key);
            }
            foreach (var t in tokensToRemove) _tokenToSession.Remove(t);

            var sessionsToRemove = new List<(string, int)>();
            foreach (var kvp in _sessionToPeer)
            {
                if (kvp.Key.roomId == roomId) sessionsToRemove.Add(kvp.Key);
            }
            foreach (var s in sessionsToRemove) _sessionToPeer.Remove(s);

            // 2. Remove the room itself
            _rooms.Remove(roomId);
            
            GD.Print($"NetworkManager: Room {roomId} and all associated data removed.");
        }

        public void CreateRoomOnServer()
        {
            RpcId(1, nameof(RequestCreateRoom));
        }

        public void JoinRoomOnServer(string code, string reconnectToken = "")
        {
            RpcId(1, nameof(RequestJoinRoom), code, reconnectToken);
        }

        public void RefreshRoomList()
        {
            RpcId(1, nameof(RequestPublicRooms));
        }

        // RPC Handlers for Clients to call on Server
        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestCreateRoom()
        {
            if (!Multiplayer.IsServer()) return;
            int peerId = Multiplayer.GetRemoteSenderId();

            if (IsThrottled(peerId)) return;
            
            // Generate a unique room code
            string code = GD.RandRange(1000, 9999).ToString();
            
            // Auto-Test Mode Override
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            if (args.Contains("--test-marathon-host"))
            {
                code = "9999";
            }
            
            // For automated testing
            bool isTest = false;
            foreach(var arg in args) if(arg == "--test-mode" || arg == "--test-marathon-host" || arg == "--server") isTest = true;
            if (isTest) code = "9999";

            while (_rooms.ContainsKey(code) && code != "9999")
            {
                code = GD.RandRange(1000, 9999).ToString();
            }

            var match = new MatchManager();
            SetupRoomEvents(code, match);
            _rooms[code] = match;
            
            GD.Print($"NetworkManager: Created room {code} for peer {peerId}");
            
            JoinRoomLogic(peerId, code);
        }

        private void SetupRoomEvents(string code, MatchManager match)
        {
            match.OnAutoMoveExecuted += () => {
                GD.Print($"NetworkManager: Auto-move executed in room {code}. Broadcasting state.");
                BroadcastGameStateInRoom(code);
            };

            match.OnTileDrawn += (pid, tile, fromDiscard, targetIndex, isDrag) => {
                Rpc(nameof(NotifyTileDrawn), pid, fromDiscard, targetIndex, tile?.Id ?? "", tile?.Value ?? 0, (int)(tile?.Color ?? 0), isDrag);
            };

            match.OnTileDiscarded += (pid, tile, rackIndex) => {
                Rpc(nameof(NotifyTileDiscarded), pid, rackIndex, tile?.Id ?? "", tile?.Value ?? 0, (int)(tile?.Color ?? 0));
            };
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestJoinRoom(string code, string reconnectToken = "")
        {
            if (!Multiplayer.IsServer()) return;
            int peerId = Multiplayer.GetRemoteSenderId();

            if (IsThrottled(peerId)) return;

            if (!_rooms.ContainsKey(code))
            {
                RpcId(peerId, nameof(NotifyRoomError), "Room not found.");
                return;
            }

            if (!string.IsNullOrEmpty(reconnectToken))
            {
                if (_tokenToSession.TryGetValue(reconnectToken, out var session) && session.roomId == code)
                {
                    HandleReconnection(peerId, code, session.playerIndex);
                    return;
                }
                else
                {
                    RpcId(peerId, nameof(NotifyRoomError), "Invalid reconnection token.");
                    return;
                }
            }

            JoinRoomLogic(peerId, code);
        }

        private void HandleReconnection(int peerId, string roomId, int playerIndex)
        {
            // Multi-device policy: Invalidate old or reject new
            if (_sessionToPeer.TryGetValue((roomId, playerIndex), out int oldPeerId))
            {
                GD.Print($"NetworkManager: Player {playerIndex} reconnecting. Invalidating old peer {oldPeerId}");
                _peer.DisconnectPeer(oldPeerId);
                _peerToRoom.Remove(oldPeerId);
                _peerToPlayer.Remove(oldPeerId);
            }

            var match = _rooms[roomId];
            var player = match.Players[playerIndex];
            player.ConnectionState = PlayerConnectionState.RECONNECTED;
            
            _peerToRoom[peerId] = roomId;
            _peerToPlayer[peerId] = playerIndex;
            _sessionToPeer[(roomId, playerIndex)] = peerId;

            RpcId(peerId, nameof(ConfirmRoomJoined), roomId, playerIndex);
            GD.Print($"NetworkManager: Peer {peerId} reconnected to room {roomId} as Player {playerIndex}");
            BroadcastGameStateInRoom(roomId);
        }

        private void JoinRoomLogic(int peerId, string code)
        {
            var match = _rooms[code];
            
            // For now, simple auto-assign to first available player slot
            int playerIndex = match.Players.Count;
            if (playerIndex >= 4)
            {
                RpcId(peerId, nameof(NotifyRoomError), "Room is full.");
                return;
            }

            string playerId = $"p{playerIndex}";
            string token = Guid.NewGuid().ToString();
            var player = new Player(playerId, peerId.ToString(), ""); // Use peer ID as name for now
            player.ReconnectToken = token;
            player.SeatIndex = playerIndex;
            match.AddPlayer(player);
            
            _peerToRoom[peerId] = code;
            _peerToPlayer[peerId] = playerIndex;
            _tokenToSession[token] = (code, playerIndex);
            _sessionToPeer[(code, playerIndex)] = peerId;

            RpcId(peerId, nameof(ConfirmRoomJoined), code, playerIndex);
            RpcId(peerId, nameof(ReceiveReconnectToken), token);
            
            GD.Print($"NetworkManager: Peer {peerId} joined room {code}. Current players: {match.Players.Count}/4");
        }

        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public void ReceiveReconnectToken(string token)
        {
            if (Multiplayer.IsServer()) return;
            // Client should store this token (e.g. in local storage or a singleton)
            GD.Print($"NetworkManager: Received reconnect token: {token}");
        }

        // RPC Stubs for Server to call on Clients
        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public void ConfirmRoomJoined(string code, int playerIndex)
        {
            if (Multiplayer.IsServer()) return;
            LocalPlayerIndex = playerIndex;
            GD.Print($"NetworkManager: Successfully joined room {code} as player {playerIndex}");
            EmitSignal(SignalName.RoomJoined, code, playerIndex);
            GetTree().ChangeSceneToFile(MainGameScenePath);
        }

        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public void NotifyWinCheckResult(bool success, string message)
        {
            if (Multiplayer.IsServer()) return;
            EmitSignal(SignalName.WinCheckResultReceived, success, message);
        }

        public void NotifyRoomError(string message)
        {
            if (Multiplayer.IsServer()) return;
            EmitSignal(SignalName.RoomError, message);
        }

        public void ConnectToServer(string ip, int port)
        {
            GD.Print($"NetworkManager: ConnectToServer requested for {ip}:{port}");
            
            // 1. Hard reset any existing connections properly
            Disconnect();
            
            GD.Print($"NetworkManager: Starting fresh connection to {ip}:{port}...");
            
            // 2. Re-instantiate peer
            var nextPeer = new ENetMultiplayerPeer();
            var error = nextPeer.CreateClient(ip, port);
            
            if (error != Error.Ok)
            {
                GD.PrintErr($"NetworkManager: Error creating ENet client: {error}");
                EmitSignal(SignalName.ConnectionFailed);
                return;
            }
            
            // 3. Assign peer deferredly to ensure the previous peer is fully cleared from Godot's internals
            _peer = nextPeer;
            CallDeferred(nameof(SetDeferredPeer), _peer);
        }

        private void SetDeferredPeer(MultiplayerPeer newPeer)
        {
            // Re-bind signals to the persistent Multiplayer object for this node
            try {
                Multiplayer.ConnectedToServer -= OnConnectedToServer;
                Multiplayer.ConnectionFailed -= OnConnectionFailed;
                Multiplayer.ServerDisconnected -= OnServerDisconnected;
            } catch { }

            Multiplayer.ConnectedToServer += OnConnectedToServer;
            Multiplayer.ConnectionFailed += OnConnectionFailed;
            Multiplayer.ServerDisconnected += OnServerDisconnected;

            Multiplayer.MultiplayerPeer = newPeer;
            GD.Print($"NetworkManager: Peer assigned DEFERRED. Status: {newPeer.GetConnectionStatus()}");
        }

        private void OnConnectedToServer()
        {
            GD.Print("NetworkManager: Connected to server!");
            EmitSignal(SignalName.ConnectionSuccessful);
        }

        private void OnConnectionFailed()
        {
            GD.PrintErr("NetworkManager: Connection failed!");
            EmitSignal(SignalName.ConnectionFailed);
        }

        private void OnServerDisconnected()
        {
            GD.Print("NetworkManager: Server disconnected!");
            EmitSignal(SignalName.ServerDisconnected);
        }

        // RPC Stubs for Server to call on Clients
        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public void SyncBoardState(string jsonState)
        {
            if (Multiplayer.IsServer()) return;
            EmitSignal(SignalName.BoardStateSynced, jsonState);
        }

        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public void SyncPrivateRack(string jsonRack)
        {
            if (Multiplayer.IsServer()) return;
            EmitSignal(SignalName.PrivateRackSynced, jsonRack);
        }

        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public void SyncRoomList(string jsonList)
        {
            if (Multiplayer.IsServer()) return;
            EmitSignal(SignalName.RoomListReceived, jsonList);
        }

        // RPC Handlers for Clients to call on Server
        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestPublicRooms()
        {
            if (!Multiplayer.IsServer()) return;
            int senderId = Multiplayer.GetRemoteSenderId();

            if (IsThrottled(senderId)) return;

            var roomList = new RoomListSyncData { Rooms = new List<RoomInfo>() };
            foreach (var kvp in _rooms)
            {
                roomList.Rooms.Add(new RoomInfo
                {
                    Code = kvp.Key,
                    PlayerCount = kvp.Value.Players.Count,
                    Status = kvp.Value.Status == GameStatus.Menu ? "Lobby" : "In Progress"
                });
            }

            string json = System.Text.Json.JsonSerializer.Serialize(roomList);
            RpcId(senderId, nameof(SyncRoomList), json);
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestDrawFromDeck(int targetIndex = -1)
        {
            if (!Multiplayer.IsServer()) return;
            
            int senderId = Multiplayer.GetRemoteSenderId();
            if (!_peerToRoom.ContainsKey(senderId)) return;

            string roomId = _peerToRoom[senderId];
            var match = _rooms[roomId];
            int playerIndex = _peerToPlayer[senderId];
            string pid = GetPlayerIdFromIndex(playerIndex);

            if (targetIndex < -1 || targetIndex >= 26) return;

            if (match.CurrentPlayerIndex == playerIndex && match.CurrentPhase == TurnPhase.Draw)
            {
                match.ResetConsecutiveMissedTurns(pid);
                bool wasDrag = targetIndex != -1;
                int landedIndex = match.DrawFromDeck(pid, targetIndex, wasDrag);
                if (landedIndex != -1)
                {
                    BroadcastGameStateInRoom(roomId);
                }
            }
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestDrawFromDiscard(int targetIndex = -1)
        {
            if (!Multiplayer.IsServer()) return;

            int senderId = Multiplayer.GetRemoteSenderId();
            if (!_peerToRoom.ContainsKey(senderId)) return;
            
            string roomId = _peerToRoom[senderId];
            var match = _rooms[roomId];
            int playerIndex = _peerToPlayer[senderId];
            string pid = GetPlayerIdFromIndex(playerIndex);

            if (targetIndex < -1 || targetIndex >= 26) return;

            if (match.CurrentPlayerIndex == playerIndex && match.CurrentPhase == TurnPhase.Draw)
            {
                match.ResetConsecutiveMissedTurns(pid);
                bool wasDrag = targetIndex != -1;
                int landedIndex = match.DrawFromDiscard(pid, targetIndex, wasDrag);
                if (landedIndex != -1)
                {
                    BroadcastGameStateInRoom(roomId);
                }
            }
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestDiscard(int rackIndex)
        {
            if (!Multiplayer.IsServer()) return;

            int senderId = Multiplayer.GetRemoteSenderId();
            if (!_peerToRoom.ContainsKey(senderId)) return;

            string roomId = _peerToRoom[senderId];
            var match = _rooms[roomId];
            int playerIndex = _peerToPlayer[senderId];
            string pid = GetPlayerIdFromIndex(playerIndex);

            GD.Print($"NetworkManager: Discard request from {senderId} (index {playerIndex}) for rack index {rackIndex}. Match Status: {match.Status}, Active: {match.CurrentPlayerIndex}, Phase: {match.CurrentPhase}");

            if (rackIndex < 0 || rackIndex >= 26) return;

            if (match.Status == GameStatus.Playing && match.CurrentPlayerIndex == playerIndex && match.CurrentPhase == TurnPhase.Discard)
            {
                match.ResetConsecutiveMissedTurns(pid);
                if (match.DiscardTile(pid, rackIndex))
                {
                    GD.Print($"NetworkManager: Discard successful for {pid}. Broadcasting state.");
                    BroadcastGameStateInRoom(roomId);
                }
                else
                {
                    GD.PrintErr($"NetworkManager: match.DiscardTile failed for {pid} at index {rackIndex}");
                }
            }
        }

        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public void NotifyTileDrawn(string playerId, bool fromDiscard, int targetSlotIndex, string tileId, int value, int color, bool wasDrag)
        {
            if (Multiplayer.IsServer()) return;
            EmitSignal(SignalName.TileDrawn, playerId, fromDiscard, targetSlotIndex, tileId, value, color, wasDrag);
        }

        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public void NotifyTileDiscarded(string playerId, int rackIndex, string tileId, int value, int color)
        {
            if (Multiplayer.IsServer()) return;
            EmitSignal(SignalName.TileDiscarded, playerId, rackIndex, tileId, value, color);
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestMoveTile(int fromIndex, int toIndex)
        {
            if (!Multiplayer.IsServer()) return;
            if (fromIndex < 0 || fromIndex >= 26 || toIndex < 0 || toIndex >= 26) return;
            
            int senderId = Multiplayer.GetRemoteSenderId();
            if (!_peerToRoom.ContainsKey(senderId)) return;

            string roomId = _peerToRoom[senderId];
            var match = _rooms[roomId];
            int playerIndex = _peerToPlayer[senderId];
            var player = match.Players[playerIndex];

            player.MoveTile(fromIndex, toIndex);
            match.ResetConsecutiveMissedTurns(player.Id);
            // No need to broadcast, client already did the move locally.
            // This just keeps server's record up to date for future syncs.
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestAddBot()
        {
            if (!Multiplayer.IsServer()) return;
            
            int senderId = Multiplayer.GetRemoteSenderId();
            if (!_peerToRoom.ContainsKey(senderId)) return;

            string roomId = _peerToRoom[senderId];
            var match = _rooms[roomId];

            // Only allow host (player index 0) to add bots
            if (_peerToPlayer[senderId] != 0) return;

            if (match.Players.Count < 4)
            {
                int botIndex = match.Players.Count;
                var bot = new Core.Application.BotPlayer($"Bot_{botIndex}", $"Bot {botIndex}", "res://Assets/avatar.png", match);
                match.AddPlayer(bot);
                
                GD.Print($"NetworkManager: Added Bot to Room {roomId}. Total players: {match.Players.Count}");
                BroadcastGameStateInRoom(roomId);
            }
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestStartGame()
        {
            if (!Multiplayer.IsServer()) return;
            int senderId = Multiplayer.GetRemoteSenderId();
            if (!_peerToRoom.ContainsKey(senderId)) return;

            string code = _peerToRoom[senderId];
            var match = _rooms[code];

            int playerIndex = _peerToPlayer[senderId];
            if (playerIndex != 0) 
            {
                GD.Print($"NetworkManager: Non-host {senderId} (index {playerIndex}) tried to start room {code}");
                return;
            }

            if (match.Status != GameStatus.Menu) return;

            if (match.Players.Count < 2)
            {
                GD.Print($"NetworkManager: Host {senderId} tried to start room {code} with only {match.Players.Count} players.");
                return;
            }

            GD.Print($"NetworkManager: Host {senderId} starting room {code} with {match.Players.Count} players.");
            match.StartGame();
            BroadcastGameStateInRoom(code);
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestCheckWinCondition(int rackIndex)
        {
            if (!Multiplayer.IsServer()) return;
            if (rackIndex < 0 || rackIndex >= 26) return;

            int senderId = Multiplayer.GetRemoteSenderId();
            if (!_peerToRoom.ContainsKey(senderId)) return;

            string roomId = _peerToRoom[senderId];
            var match = _rooms[roomId];
            int playerIndex = _peerToPlayer[senderId];
            string pid = GetPlayerIdFromIndex(playerIndex);

            if (match.CurrentPlayerIndex == playerIndex && match.CurrentPhase == TurnPhase.Discard)
            {
                match.ResetConsecutiveMissedTurns(pid);
                var (success, message) = match.FinishGame(pid, rackIndex);
                if (success)
                {
                    BroadcastGameStateInRoom(roomId);
                }
                
                // Always notify the requester of the result
                RpcId(senderId, nameof(NotifyWinCheckResult), success, message);
            }
            else
            {
                RpcId(senderId, nameof(NotifyWinCheckResult), false, "Not your turn or wrong phase.");
            }
        }

        private void BroadcastGameStateInRoom(string roomId)
        {
            var match = _rooms[roomId];

            // 1. Send public board state to everyone in the room
            var boardState = new BoardSyncData
            {
                DeckCount = match.GameDeck?.RemainingCount ?? 106,
                Indicator = match.IndicatorTile,
                ActivePlayer = match.CurrentPlayerIndex,
                Phase = match.CurrentPhase,
                Status = match.Status == GameStatus.Menu ? "Lobby" : (match.Status == GameStatus.Victory ? "Victory" : "Active"),
                Discards = new List<List<Tile>>(),
                PlayerNames = new List<string>(),
                TurnStartTimestamp = match.TurnStartTimestamp,
                TurnDuration = match.TurnDuration,
                IsBot = match.Players.Select(p => p.IsBot).ToList(),
                WinnerId = match.WinnerId,
                WinnerTiles = match.WinnerTiles
            };

            foreach (var mapping in _peerToRoom)
            {
                if (mapping.Value == roomId)
                {
                    int recipientPeerId = mapping.Key;
                    int recipientPlayerIndex = _peerToPlayer[recipientPeerId];

                    // Security: Obfuscate opponent discard piles (only top card visible)
                    var perPeerDiscards = new List<List<Tile>>();
                    for (int i = 0; i < match.Players.Count; i++)
                    {
                        var pOwner = match.Players[i];
                        var originalPile = match.PlayerDiscardPiles.ContainsKey(pOwner.Id) ? match.PlayerDiscardPiles[pOwner.Id] : new List<Tile>();
                        
                        if (i == recipientPlayerIndex)
                        {
                            perPeerDiscards.Add(new List<Tile>(originalPile)); // Owner sees all
                        }
                        else
                        {
                            var topOnly = new List<Tile>();
                            if (originalPile.Count > 0) topOnly.Add(originalPile[^1]); // Others see only top
                            perPeerDiscards.Add(topOnly);
                        }
                    }
                    boardState.Discards = perPeerDiscards;

                    string jsonBoard = System.Text.Json.JsonSerializer.Serialize(boardState);
                    RpcId(recipientPeerId, nameof(SyncBoardState), jsonBoard);
                    
                    // 2. Send private rack
                    int playerIndex = _peerToPlayer[recipientPeerId];
                    var player = match.Players[playerIndex];
                    
                    var rackSync = new RackSyncData { Slots = new List<Tile>(player.Rack) };
                    
                    // Security: Only reveal the next tile if it's the recipient's turn and they are in the draw phase
                    if (match.CurrentPlayerIndex == playerIndex && match.CurrentPhase == TurnPhase.Draw)
                    {
                        rackSync.NextDeckTile = match.PeekDeck();
                    }

                    string jsonRack = System.Text.Json.JsonSerializer.Serialize(rackSync);
                    RpcId(recipientPeerId, nameof(SyncPrivateRack), jsonRack);
                }
            }
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestSync()
        {
            if (!Multiplayer.IsServer()) return;
            int senderId = Multiplayer.GetRemoteSenderId();
            if (!_peerToRoom.ContainsKey(senderId)) return;

            string roomId = _peerToRoom[senderId];
            GD.Print($"NetworkManager: Resync requested by peer {senderId} in room {roomId}");
            BroadcastGameStateInRoom(roomId);
        }

        private string GetPlayerIdFromIndex(int index)
        {
            return $"p{index}";
        }

        private bool IsThrottled(int peerId, int cooldownMs = 1000)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_lastRpcTime.TryGetValue(peerId, out long lastTime))
            {
                if (now - lastTime < cooldownMs)
                {
                    GD.Print($"NetworkManager: Throttling peer {peerId} (Room RPC)");
                    return true;
                }
            }
            _lastRpcTime[peerId] = now;
            return false;
        }
    }
}
