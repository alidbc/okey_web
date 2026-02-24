using System.Collections.Generic;
using OkieRummyGodot.Core.Domain;

namespace OkieRummyGodot.Core.Networking
{
    public class BoardSyncData
    {
        public int DeckCount { get; set; }
        public Tile Indicator { get; set; }
        public int ActivePlayer { get; set; }
        public TurnPhase Phase { get; set; }
        public string Status { get; set; } // "Lobby", "InGame", etc.
        public List<List<Tile>> Discards { get; set; }
        public List<string> PlayerNames { get; set; }
        public List<bool> IsBot { get; set; }
        public List<bool> IsDisconnected { get; set; }
        public long TurnStartTimestamp { get; set; }
        public int TurnDuration { get; set; }
        public string WinnerId { get; set; }
        public List<Tile> WinnerTiles { get; set; }
    }

    public class RackSyncData
    {
        public List<Tile> Slots { get; set; }
        public Tile NextDeckTile { get; set; }
    }

    public class RoomInfo
    {
        public string Code { get; set; }
        public int PlayerCount { get; set; }
        public string Status { get; set; } // "Lobby", "InGame", etc.
    }

    public class RoomListSyncData
    {
        public List<RoomInfo> Rooms { get; set; }
    }
}
