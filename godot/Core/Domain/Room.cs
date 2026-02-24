using System.Collections.Generic;
using OkieRummyGodot.Core.Application;

namespace OkieRummyGodot.Core.Domain
{
    public class Room
    {
        public string Code { get; set; }
        public List<Player> Players { get; set; } = new List<Player>();
        public MatchManager ActiveMatch { get; set; }
        public bool IsGaming => ActiveMatch != null && ActiveMatch.Status != GameStatus.Menu;

        public Room(string code)
        {
            Code = code;
        }

        public void AddPlayer(Player player)
        {
            Players.Add(player);
        }

        public void RemovePlayer(int index)
        {
            if (index >= 0 && index < Players.Count)
            {
                Players.RemoveAt(index);
            }
        }
    }
}
