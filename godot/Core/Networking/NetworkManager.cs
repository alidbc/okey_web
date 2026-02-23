using Godot;
using System;
using System.Collections.Generic;
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

        [Export] public string MainGameScenePath = "res://UI/Scenes/Main.tscn";
        
        public int LocalPlayerIndex { get; private set; } = -1;
        private Dictionary<string, MatchManager> _rooms = new Dictionary<string, MatchManager>();
        private Dictionary<int, string> _peerToRoom = new Dictionary<int, string>();
        private Dictionary<int, int> _peerToPlayer = new Dictionary<int, int>(); // peerId -> playerIndex (0-3) within their room
        private Dictionary<string, (string roomId, int playerIndex)> _tokenToSession = new Dictionary<string, (string, int)>();
        private Dictionary<(string roomId, int playerIndex), int> _sessionToPeer = new Dictionary<(string, int), int>();

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
            
            // Generate a unique room code
            string code = GD.RandRange(1000, 9999).ToString();
            
            // For automated testing
            bool isTest = false;
            foreach(var arg in OS.GetCmdlineArgs()) if(arg == "--test-mode") isTest = true;
            if (isTest) code = "TEST";

            while (_rooms.ContainsKey(code) && code != "TEST")
            {
                code = GD.RandRange(1000, 9999).ToString();
            }

            var match = new MatchManager();
            _rooms[code] = match;
            
            GD.Print($"NetworkManager: Created room {code} for peer {peerId}");
            
            JoinRoomLogic(peerId, code);
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestJoinRoom(string code, string reconnectToken = "")
        {
            if (!Multiplayer.IsServer()) return;
            int peerId = Multiplayer.GetRemoteSenderId();

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
            GD.Print($"NetworkManager: Connecting to {ip}:{port}...");
            var error = _peer.CreateClient(ip, port);
            if (error != Error.Ok)
            {
                GD.PrintErr($"NetworkManager: Error creating client: {error}");
                EmitSignal(SignalName.ConnectionFailed);
                return;
            }
            Multiplayer.MultiplayerPeer = _peer;
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

            if (match.CurrentPlayerIndex == playerIndex && match.CurrentPhase == TurnPhase.Draw)
            {
                if (match.DrawFromDeck(pid, targetIndex) != -1)
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

            if (match.CurrentPlayerIndex == playerIndex && match.CurrentPhase == TurnPhase.Draw)
            {
                if (match.DrawFromDiscard(pid, targetIndex) != -1)
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

            if (match.Status == GameStatus.Playing && match.CurrentPlayerIndex == playerIndex && match.CurrentPhase == TurnPhase.Discard)
            {
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
            else
            {
            }
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestMoveTile(int fromIndex, int toIndex)
        {
            if (!Multiplayer.IsServer()) return;
            
            int senderId = Multiplayer.GetRemoteSenderId();
            if (!_peerToRoom.ContainsKey(senderId)) return;

            string roomId = _peerToRoom[senderId];
            var match = _rooms[roomId];
            int playerIndex = _peerToPlayer[senderId];
            var player = match.Players[playerIndex];

            player.MoveTile(fromIndex, toIndex);
            // No need to broadcast, client already did the move locally.
            // This just keeps server's record up to date for future syncs.
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

            int senderId = Multiplayer.GetRemoteSenderId();
            if (!_peerToRoom.ContainsKey(senderId)) return;

            string roomId = _peerToRoom[senderId];
            var match = _rooms[roomId];
            int playerIndex = _peerToPlayer[senderId];
            string pid = GetPlayerIdFromIndex(playerIndex);

            if (match.CurrentPlayerIndex == playerIndex && match.CurrentPhase == TurnPhase.Discard)
            {
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
                WinnerId = match.WinnerId,
                WinnerTiles = match.WinnerTiles
            };

            // Order discards
            foreach (var p in match.Players)
            {
                if (match.PlayerDiscardPiles.ContainsKey(p.Id))
                {
                    boardState.Discards.Add(match.PlayerDiscardPiles[p.Id]);
                }
                else
                {
                    GD.PrintErr($"NetworkManager: Player {p.Id} has no discard pile in room {roomId}. Using empty list.");
                    boardState.Discards.Add(new List<Tile>());
                }
                boardState.PlayerNames.Add(p.Name);
            }

            string jsonBoard = System.Text.Json.JsonSerializer.Serialize(boardState);
            
            // Collect peers in this room
            foreach (var mapping in _peerToRoom)
            {
                if (mapping.Value == roomId)
                {
                    RpcId(mapping.Key, nameof(SyncBoardState), jsonBoard);
                    
                    // 2. Send private rack
                    int playerIndex = _peerToPlayer[mapping.Key];
                    var player = match.Players[playerIndex];
                    string jsonRack = System.Text.Json.JsonSerializer.Serialize(new RackSyncData { Slots = new List<Tile>(player.Rack) });
                    RpcId(mapping.Key, nameof(SyncPrivateRack), jsonRack);
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
    }
}
