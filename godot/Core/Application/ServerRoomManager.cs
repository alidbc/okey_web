using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OkieRummyGodot.Core.Domain;

namespace OkieRummyGodot.Core.Application
{
    public class ServerRoomManager
    {
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();
        private readonly Dictionary<int, string> _peerToRoom = new Dictionary<int, string>();
        private readonly Dictionary<int, int> _peerToPlayerIndex = new Dictionary<int, int>();
        private readonly Dictionary<string, (string roomId, int playerIndex)> _tokenToSession = new Dictionary<string, (string, int)>();
        private readonly Dictionary<(string roomId, int playerIndex), int> _sessionToPeer = new Dictionary<(string, int), int>();

        public IEnumerable<Room> ActiveRooms => _rooms.Values;

        public Room CreateRoom(int hostPeerId, string code)
        {
            var room = new Room(code);
            _rooms[code] = room;
            GD.Print($"ServerRoomManager: Created room {code}");
            return room;
        }

        public Room GetRoom(string code) => _rooms.TryGetValue(code, out var room) ? room : null;

        public Room GetRoomByPeer(int peerId)
        {
            return _peerToRoom.TryGetValue(peerId, out var code) ? GetRoom(code) : null;
        }

        public int GetPlayerIndex(int peerId) => _peerToPlayerIndex.TryGetValue(peerId, out var index) ? index : -1;

        public string GetPlayerName(int peerId)
        {
            var room = GetRoomByPeer(peerId);
            if (room == null) return "Unknown";
            int idx = GetPlayerIndex(peerId);
            var player = room.Players.FirstOrDefault(p => p.SeatIndex == idx);
            return player?.Name ?? "Unknown";
        }

        public bool JoinRoom(int peerId, string code, string name, string avatar, out int playerIndex, out string token, out string errorMessage)
        {
            playerIndex = -1;
            token = "";
            errorMessage = "";

            var room = GetRoom(code);
            if (room == null)
            {
                errorMessage = "Room not found.";
                return false;
            }

            if (room.IsGaming)
            {
                errorMessage = "Room already in game.";
                return false;
            }

            if (room.Players.Count >= 4)
            {
                errorMessage = "Room is full.";
                return false;
            }

            playerIndex = room.Players.Count;
            token = Guid.NewGuid().ToString();
            string playerId = $"p{playerIndex}";
            
            // Use provided name/avatar or fallback
            string finalName = !string.IsNullOrWhiteSpace(name) ? name : $"Player {playerIndex + 1}";
            string finalAvatar = !string.IsNullOrWhiteSpace(avatar) ? avatar : "res://Assets/avatar.png";

            var player = new Player(playerId, finalName, finalAvatar)
            {
                SeatIndex = playerIndex,
                ReconnectToken = token
            };
            
            room.AddPlayer(player);
            _peerToRoom[peerId] = code;
            _peerToPlayerIndex[peerId] = playerIndex;
            _tokenToSession[token] = (code, playerIndex);
            _sessionToPeer[(code, playerIndex)] = peerId;

            GD.Print($"ServerRoomManager: Peer {peerId} ({finalName}) joined room {code} as Player {playerIndex}");
            return true;
        }

        public bool Reconnect(int peerId, string token, out string roomId, out int playerIndex, out string errorMessage)
        {
            roomId = "";
            playerIndex = -1;
            errorMessage = "";

            if (!_tokenToSession.TryGetValue(token, out var session))
            {
                errorMessage = "Invalid reconnection token.";
                return false;
            }

            roomId = session.roomId;
            playerIndex = session.playerIndex;
            var room = GetRoom(roomId);

            if (room == null)
            {
                errorMessage = "Room no longer exists.";
                return false;
            }

            // Invalidate old peer if still registered
            if (_sessionToPeer.TryGetValue((roomId, playerIndex), out int oldPeerId))
            {
                _peerToRoom.Remove(oldPeerId);
                _peerToPlayerIndex.Remove(oldPeerId);
            }

            _peerToRoom[peerId] = roomId;
            _peerToPlayerIndex[peerId] = playerIndex;
            _sessionToPeer[(roomId, playerIndex)] = peerId;

            var player = room.Players[playerIndex];
            player.ConnectionState = PlayerConnectionState.RECONNECTED;

            GD.Print($"ServerRoomManager: Peer {peerId} reconnected to room {roomId} as Player {playerIndex}");
            return true;
        }

        public (string roomId, int playerIndex, bool roomCleared) HandleDisconnect(int peerId)
        {
            if (!_peerToRoom.TryGetValue(peerId, out var roomId)) return (null, -1, false);
            if (!_peerToPlayerIndex.TryGetValue(peerId, out var playerIndex)) return (null, -1, false);

            var room = GetRoom(roomId);
            if (room == null) return (null, -1, false);

            bool roomCleared = false;

            if (!room.IsGaming)
            {
                // Full removal in lobby
                var pRemoving = room.Players[playerIndex];
                _tokenToSession.Remove(pRemoving.ReconnectToken);
                room.Players.RemoveAt(playerIndex);
                
                GD.Print($"ServerRoomManager: Player {playerIndex} removed from lobby {roomId}. Shifting...");

                // Shifting
                var roomPeers = _peerToRoom.Where(kvp => kvp.Value == roomId).Select(kvp => kvp.Key).ToList();
                foreach (var pId in roomPeers)
                {
                    if (pId == peerId) continue;
                    int currentIdx = _peerToPlayerIndex[pId];
                    if (currentIdx > playerIndex)
                    {
                        int newIdx = currentIdx - 1;
                        var p = room.Players[newIdx];
                        
                        // Update session mappings
                        _sessionToPeer.Remove((roomId, currentIdx));
                        _sessionToPeer[(roomId, newIdx)] = pId;
                        
                        // Update token mapping
                        _tokenToSession[p.ReconnectToken] = (roomId, newIdx);
                        
                        // Update player & lookup
                        _peerToPlayerIndex[pId] = newIdx;
                        p.SeatIndex = newIdx;
                        p.Id = $"p{newIdx}";
                    }
                }
            }
            else
            {
                // Just mark as disconnected if in game
                room.Players[playerIndex].ConnectionState = PlayerConnectionState.TEMP_DISCONNECTED;
            }

            _peerToRoom.Remove(peerId);
            _peerToPlayerIndex.Remove(peerId);
            _sessionToPeer.Remove((roomId, playerIndex));

            if (!_peerToRoom.ContainsValue(roomId))
            {
                CleanupRoom(roomId);
                roomCleared = true;
            }

            return (roomId, playerIndex, roomCleared);
        }

        private void CleanupRoom(string roomId)
        {
            var tokensToRemove = _tokenToSession.Where(kvp => kvp.Value.roomId == roomId).Select(kvp => kvp.Key).ToList();
            foreach (var t in tokensToRemove) _tokenToSession.Remove(t);

            var sessionsToRemove = _sessionToPeer.Keys.Where(k => k.roomId == roomId).ToList();
            foreach (var s in sessionsToRemove) _sessionToPeer.Remove(s);

            _rooms.Remove(roomId);
            GD.Print($"ServerRoomManager: Room {roomId} fully cleaned up.");
        }

        public void StartMatch(string code)
        {
            var room = GetRoom(code);
            if (room == null || room.Players.Count < 2) return;

            room.ActiveMatch = new MatchManager();
            foreach (var p in room.Players)
            {
                room.ActiveMatch.AddPlayer(p);
            }
            room.ActiveMatch.StartGame();
        }

        public int GetPeerId(string roomId, int playerIndex)
        {
            return _sessionToPeer.TryGetValue((roomId, playerIndex), out int peerId) ? peerId : -1;
        }

        public List<int> GetPeersInRoom(string roomId)
        {
            return _peerToRoom.Where(kvp => kvp.Value == roomId).Select(kvp => kvp.Key).ToList();
        }
    }
}
