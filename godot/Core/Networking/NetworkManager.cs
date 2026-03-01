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
        [Signal] public delegate void IndicatorShownEventHandler(string playerId, string tileId, int value, int color);

        [Export] public string MainGameScenePath = "res://UI/Scenes/Main.tscn";
        
        public int LocalPlayerIndex { get; private set; } = -1;
        private ServerRoomManager _roomManager = new ServerRoomManager();
        private Dictionary<int, long> _lastRpcTime = new Dictionary<int, long>();
        private double _tickTimer = 0;

        public bool IsActive => Multiplayer.MultiplayerPeer != null && 
                                 Multiplayer.MultiplayerPeer is ENetMultiplayerPeer &&
                                 Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

        public bool IsHost()
        {
            if (Multiplayer.IsServer())
            {
                int senderId = Multiplayer.GetRemoteSenderId();
                return _roomManager.GetPlayerIndex(senderId) == 0;
            }
            return LocalPlayerIndex == 0;
        }

        public string GetRoomCode(int peerId) => _roomManager.GetRoomByPeer(peerId)?.Code ?? "";

        public override void _Ready()
        {
            ForceReady();
        }

        public override void _Process(double delta)
        {
            var currentStatus = Multiplayer.MultiplayerPeer?.GetConnectionStatus() ?? MultiplayerPeer.ConnectionStatus.Disconnected;
            if (currentStatus != _lastStatus)
            {
                GD.Print($"NetworkManager: ConnectionStatus changed from {_lastStatus} to {currentStatus}");
                _lastStatus = currentStatus;
            }

            // Guard: IsServer() throws when peer is not fully connected
            if (currentStatus != MultiplayerPeer.ConnectionStatus.Connected) return;
            if (!Multiplayer.IsServer()) return;

            _tickTimer += delta;
            if (_tickTimer >= 1.0)
            {
                _tickTimer = 0;
                foreach (var room in _roomManager.ActiveRooms)
                {
                    room.ActiveMatch?.CheckTimeouts();
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
            
            if (Multiplayer.MultiplayerPeer != null && !(Multiplayer.MultiplayerPeer is OfflineMultiplayerPeer))
            {
                try {
                    Multiplayer.ConnectedToServer -= OnConnectedToServer;
                    Multiplayer.ConnectionFailed -= OnConnectionFailed;
                    Multiplayer.ServerDisconnected -= OnServerDisconnected;
                } catch { }

                if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet)
                {
                    enet.Close();
                }
            }
            
            Multiplayer.MultiplayerPeer = null;
            _peer = null;

            LocalPlayerIndex = -1;
            _roomManager = new ServerRoomManager();
            _lastRpcTime.Clear();
            _tickTimer = 0;
            _lastStatus = MultiplayerPeer.ConnectionStatus.Disconnected;
        }

        private void OnPeerDisconnectedOnServer(long id)
        {
            int peerId = (int)id;
            var (roomId, playerIndex, roomCleared) = _roomManager.HandleDisconnect(peerId);
            
            if (roomId != null)
            {
                GD.Print($"NetworkManager: Peer {peerId} disconnected from room {roomId}");
                if (!roomCleared)
                {
                    BroadcastGameStateInRoom(roomId);
                }
            }
        }

        public void CreateRoomOnServer(string name = "", string avatar = "") => RpcId(1, nameof(RequestCreateRoom), name, avatar);
        public void JoinRoomOnServer(string code, string reconnectToken = "", string name = "", string avatar = "") => RpcId(1, nameof(RequestJoinRoom), code, reconnectToken, name, avatar);
        public void RefreshRoomList() => RpcId(1, nameof(RequestPublicRooms));

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestCreateRoom(string name = "", string avatar = "")
        {
            if (!Multiplayer.IsServer()) return;
            int peerId = Multiplayer.GetRemoteSenderId();
            if (IsThrottled(peerId)) return;
            
            string code = GD.RandRange(1000, 9999).ToString();
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            bool isTest = args.Contains("--test-mode") || args.Contains("--test-marathon-host") || args.Contains("--server");
            if (isTest) code = "9999";

            while (_roomManager.GetRoom(code) != null && code != "9999")
            {
                code = GD.RandRange(1000, 9999).ToString();
            }

            _roomManager.CreateRoom(peerId, code);
            GD.Print($"NetworkManager: Created room {code} for peer {peerId}");
            JoinRoomLogic(peerId, code, name, avatar);
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestJoinRoom(string code, string reconnectToken = "", string name = "", string avatar = "")
        {
            if (!Multiplayer.IsServer()) return;
            int peerId = Multiplayer.GetRemoteSenderId();
            if (IsThrottled(peerId)) return;

            if (!string.IsNullOrEmpty(reconnectToken))
            {
                if (_roomManager.Reconnect(peerId, reconnectToken, out var roomId, out var playerIndex, out var error))
                {
                    RpcId(peerId, nameof(ConfirmRoomJoined), roomId, playerIndex);
                    BroadcastGameStateInRoom(roomId);
                    return;
                }
                else
                {
                    RpcId(peerId, nameof(NotifyRoomError), error);
                    return;
                }
            }

            JoinRoomLogic(peerId, code, name, avatar);
        }

        private void JoinRoomLogic(int peerId, string code, string name = "", string avatar = "")
        {
            if (_roomManager.JoinRoom(peerId, code, name, avatar, out int playerIndex, out string token, out string error))
            {
                RpcId(peerId, nameof(ConfirmRoomJoined), code, playerIndex);
                RpcId(peerId, nameof(ReceiveReconnectToken), token);
                GD.Print($"NetworkManager: Peer {peerId} joined room {code}. Index: {playerIndex}");
                BroadcastGameStateInRoom(code);
            }
            else
            {
                RpcId(peerId, nameof(NotifyRoomError), error);
            }
        }

        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public async void ConfirmRoomJoined(string code, int playerIndex)
        {
            if (Multiplayer.IsServer()) return;
            LocalPlayerIndex = playerIndex;
            GD.Print($"NetworkManager: Successfully joined room {code} as player {playerIndex}");
            EmitSignal(SignalName.RoomJoined, code, playerIndex);

            // Delay scene transition to give LobbyUI's async RPCs time to flush
            await ToSignal(GetTree().CreateTimer(1.5f), Godot.SceneTreeTimer.SignalName.Timeout);

            GetTree().ChangeSceneToFile(MainGameScenePath);
        }

        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public void ReceiveReconnectToken(string token)
        {
            if (Multiplayer.IsServer()) return;
            GD.Print($"NetworkManager: Received reconnect token: {token}");
        }

        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public void NotifyWinCheckResult(bool success, string message)
        {
            if (Multiplayer.IsServer()) return;
            EmitSignal(SignalName.WinCheckResultReceived, success, message);
        }

        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public void NotifyRoomError(string message)
        {
            if (Multiplayer.IsServer()) return;
            EmitSignal(SignalName.RoomError, message);
        }

        // ================================================================
        // INVITE RELAY


        public void ConnectToServer(string ip, int port)
        {
            Disconnect();
            var nextPeer = new ENetMultiplayerPeer();
            if (nextPeer.CreateClient(ip, port) != Error.Ok)
            {
                EmitSignal(SignalName.ConnectionFailed);
                return;
            }
            _peer = nextPeer;
            CallDeferred(nameof(SetDeferredPeer), _peer);
        }

        private void SetDeferredPeer(MultiplayerPeer newPeer)
        {
            try {
                Multiplayer.ConnectedToServer -= OnConnectedToServer;
                Multiplayer.ConnectionFailed -= OnConnectionFailed;
                Multiplayer.ServerDisconnected -= OnServerDisconnected;
            } catch { }

            Multiplayer.ConnectedToServer += OnConnectedToServer;
            Multiplayer.ConnectionFailed += OnConnectionFailed;
            Multiplayer.ServerDisconnected += OnServerDisconnected;

            Multiplayer.MultiplayerPeer = newPeer;
        }

        private void OnConnectedToServer() => EmitSignal(SignalName.ConnectionSuccessful);
        private void OnConnectionFailed() => EmitSignal(SignalName.ConnectionFailed);
        private void OnServerDisconnected() => EmitSignal(SignalName.ServerDisconnected);

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

        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public void SyncLocalIndex(int newIndex)
        {
            if (Multiplayer.IsServer()) return;
            LocalPlayerIndex = newIndex;
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestPublicRooms()
        {
            if (!Multiplayer.IsServer()) return;
            int senderId = Multiplayer.GetRemoteSenderId();
            if (IsThrottled(senderId)) return;

            var roomList = new RoomListSyncData { Rooms = new List<RoomInfo>() };
            foreach (var room in _roomManager.ActiveRooms)
            {
                roomList.Rooms.Add(new RoomInfo {
                    Code = room.Code,
                    PlayerCount = room.Players.Count,
                    Status = !room.IsGaming ? "Lobby" : "In Progress"
                });
            }
            RpcId(senderId, nameof(SyncRoomList), System.Text.Json.JsonSerializer.Serialize(roomList));
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestDrawFromDeck(int targetIndex = -1)
        {
            if (!Multiplayer.IsServer()) return;
            int senderId = Multiplayer.GetRemoteSenderId();
            var room = _roomManager.GetRoomByPeer(senderId);
            if (room?.ActiveMatch == null) return;

            int playerIndex = _roomManager.GetPlayerIndex(senderId);
            if (room.ActiveMatch.CurrentPlayerIndex == playerIndex && room.ActiveMatch.CurrentPhase == TurnPhase.Draw)
            {
                string pid = $"p{playerIndex}";
                room.ActiveMatch.ResetConsecutiveMissedTurns(pid);
                if (room.ActiveMatch.DrawFromDeck(pid, targetIndex, targetIndex != -1) != -1)
                    BroadcastGameStateInRoom(room.Code);
            }
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestDrawFromDiscard(int targetIndex = -1)
        {
            if (!Multiplayer.IsServer()) return;
            int senderId = Multiplayer.GetRemoteSenderId();
            var room = _roomManager.GetRoomByPeer(senderId);
            if (room?.ActiveMatch == null) return;

            int playerIndex = _roomManager.GetPlayerIndex(senderId);
            if (room.ActiveMatch.CurrentPlayerIndex == playerIndex && room.ActiveMatch.CurrentPhase == TurnPhase.Draw)
            {
                string pid = $"p{playerIndex}";
                room.ActiveMatch.ResetConsecutiveMissedTurns(pid);
                if (room.ActiveMatch.DrawFromDiscard(pid, targetIndex, targetIndex != -1) != -1)
                    BroadcastGameStateInRoom(room.Code);
            }
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestDiscard(int rackIndex)
        {
            if (!Multiplayer.IsServer()) return;
            int senderId = Multiplayer.GetRemoteSenderId();
            var room = _roomManager.GetRoomByPeer(senderId);
            if (room?.ActiveMatch == null) return;

            int playerIndex = _roomManager.GetPlayerIndex(senderId);
            if (room.ActiveMatch.CurrentPlayerIndex == playerIndex && room.ActiveMatch.CurrentPhase == TurnPhase.Discard)
            {
                string pid = $"p{playerIndex}";
                room.ActiveMatch.ResetConsecutiveMissedTurns(pid);
                if (room.ActiveMatch.DiscardTile(pid, rackIndex))
                    BroadcastGameStateInRoom(room.Code);
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

        [Rpc(MultiplayerApi.RpcMode.Authority)]
        public void NotifyIndicatorShown(string playerId, string tileId, int value, int color)
        {
            if (Multiplayer.IsServer()) return;
            EmitSignal(SignalName.IndicatorShown, playerId, tileId, value, color);
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestMoveTile(int fromIndex, int toIndex)
        {
            if (!Multiplayer.IsServer()) return;
            int senderId = Multiplayer.GetRemoteSenderId();
            var room = _roomManager.GetRoomByPeer(senderId);
            if (room?.ActiveMatch != null)
            {
                int playerIndex = _roomManager.GetPlayerIndex(senderId);
                room.ActiveMatch.Players[playerIndex].MoveTile(fromIndex, toIndex);
                room.ActiveMatch.ResetConsecutiveMissedTurns(room.ActiveMatch.Players[playerIndex].Id);
            }
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestAddBot()
        {
            if (!Multiplayer.IsServer()) return;
            int senderId = Multiplayer.GetRemoteSenderId();
            var room = _roomManager.GetRoomByPeer(senderId);
            if (room == null || room.IsGaming || _roomManager.GetPlayerIndex(senderId) != 0) return;

            if (room.Players.Count < 4)
            {
                int botIndex = room.Players.Count;
                var bot = new BotPlayer($"Bot_{botIndex}", $"Bot {botIndex}", "res://Assets/avatar.png", room.ActiveMatch) 
                { 
                    SeatIndex = botIndex 
                };
                room.AddPlayer(bot);
                BroadcastGameStateInRoom(room.Code);
            }
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestStartGame()
        {
            if (!Multiplayer.IsServer()) return;
            int senderId = Multiplayer.GetRemoteSenderId();
            var room = _roomManager.GetRoomByPeer(senderId);
            if (room == null || room.IsGaming || _roomManager.GetPlayerIndex(senderId) != 0 || room.Players.Count < 2) return;

            _roomManager.StartMatch(room.Code);
            HookMatchEvents(room.Code, room.ActiveMatch);
            BroadcastGameStateInRoom(room.Code);
        }

        private void HookMatchEvents(string code, MatchManager match)
        {
            match.OnAutoMoveExecuted += () => BroadcastGameStateInRoom(code);
            match.OnTileDrawn += (pid, tile, fromDiscard, targetIndex, isDrag) => 
                Rpc(nameof(NotifyTileDrawn), pid, fromDiscard, targetIndex, tile?.Id ?? "", tile?.Value ?? 0, (int)(tile?.Color ?? 0), isDrag);
            match.OnTileDiscarded += (pid, tile, rackIndex) => 
                Rpc(nameof(NotifyTileDiscarded), pid, rackIndex, tile?.Id ?? "", tile?.Value ?? 0, (int)(tile?.Color ?? 0));
            match.OnIndicatorShown += (pid, tile) => 
                Rpc(nameof(NotifyIndicatorShown), pid, tile?.Id ?? "", tile?.Value ?? 0, (int)(tile?.Color ?? 0));
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestCheckWinCondition(int rackIndex)
        {
            if (!Multiplayer.IsServer()) return;
            int senderId = Multiplayer.GetRemoteSenderId();
            var room = _roomManager.GetRoomByPeer(senderId);
            if (room?.ActiveMatch == null) return;

            int playerIndex = _roomManager.GetPlayerIndex(senderId);
            if (room.ActiveMatch.CurrentPlayerIndex == playerIndex && room.ActiveMatch.CurrentPhase == TurnPhase.Discard)
            {
                string pid = $"p{playerIndex}";
                room.ActiveMatch.ResetConsecutiveMissedTurns(pid);
                var (success, message) = room.ActiveMatch.FinishGame(pid, rackIndex);
                if (success) BroadcastGameStateInRoom(room.Code);
                RpcId(senderId, nameof(NotifyWinCheckResult), success, message);
            }
        }

        private void BroadcastGameStateInRoom(string roomId)
        {
            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;
            var match = room.ActiveMatch;
            // Use match.Players when active (bot replacement modifies match.Players, not room.Players)
            var activePlayers = match?.Players ?? room.Players;

            var boardState = new BoardSyncData {
                DeckCount = match?.GameDeck?.RemainingCount ?? 106,
                Indicator = match?.IndicatorTile,
                ActivePlayer = match?.CurrentPlayerIndex ?? 0,
                Phase = match?.CurrentPhase ?? TurnPhase.Draw,
                Status = !room.IsGaming ? "Lobby" : (match.Status == GameStatus.Victory ? "Victory" : "Active"),
                PlayerNames = activePlayers.Select(p => p.Name).ToList(),
                PlayerAvatars = activePlayers.Select(p => p.AvatarUrl).ToList(),
                TurnStartTimestamp = match?.TurnStartTimestamp ?? 0,
                TurnDuration = match?.TurnDuration ?? 30,
                IsBot = activePlayers.Select(p => p.IsBot).ToList(),
                IsDisconnected = activePlayers.Select(p => 
                    p.ConnectionState == PlayerConnectionState.TEMP_DISCONNECTED ||
                    p.ConnectionState == PlayerConnectionState.REPLACED_BY_BOT ||
                    p.ConnectionState == PlayerConnectionState.TIMED_OUT ||
                    p.ConnectionState == PlayerConnectionState.LEFT_INTENTIONALLY
                ).ToList(),
                WinnerId = match?.WinnerId,
                WinnerTiles = match?.WinnerTiles
            };

            var peers = _roomManager.GetPeersInRoom(roomId);
            foreach (var peerId in peers)
            {
                int pIdx = _roomManager.GetPlayerIndex(peerId);
                if (pIdx == -1) continue;

                // Critical: Ensure client's local index is in sync (important for seat shifts)
                RpcId(peerId, nameof(SyncLocalIndex), pIdx);

                var perPeerDiscards = new List<List<Tile>>();
                for (int i = 0; i < room.Players.Count; i++)
                {
                    var pOwner = room.Players[i];
                    var pile = (match != null && match.PlayerDiscardPiles.TryGetValue(pOwner.Id, out var p)) ? p : new List<Tile>();
                    if (i == pIdx) perPeerDiscards.Add(new List<Tile>(pile));
                    else {
                        var top = new List<Tile>();
                        if (pile.Count > 0) top.Add(pile[^1]);
                        perPeerDiscards.Add(top);
                    }
                }
                boardState.Discards = perPeerDiscards;
                RpcId(peerId, nameof(SyncBoardState), System.Text.Json.JsonSerializer.Serialize(boardState));

                if (match != null)
                {
                    var p = room.Players[pIdx];
                    var rackSync = new RackSyncData { Slots = new List<Tile>(p.Rack) };
                    if (match.CurrentPlayerIndex == pIdx && match.CurrentPhase == TurnPhase.Draw)
                        rackSync.NextDeckTile = match.PeekDeck();
                    RpcId(peerId, nameof(SyncPrivateRack), System.Text.Json.JsonSerializer.Serialize(rackSync));
                }
            }
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
        public void RequestSync()
        {
            if (!Multiplayer.IsServer()) return;
            var room = _roomManager.GetRoomByPeer(Multiplayer.GetRemoteSenderId());
            if (room != null) BroadcastGameStateInRoom(room.Code);
        }

        private bool IsThrottled(int peerId, int cooldownMs = 1000)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_lastRpcTime.TryGetValue(peerId, out long lastTime) && now - lastTime < cooldownMs) return true;
            _lastRpcTime[peerId] = now;
            return false;
        }
    }
}
