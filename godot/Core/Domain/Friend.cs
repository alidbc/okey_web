using System;

namespace OkieRummyGodot.Core.Domain
{
    public class Friend
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; }

        public Friend() { }

        public Friend(string id, string name)
        {
            Id = id;
            Name = name;
            IsOnline = false;
            LastSeen = DateTime.Now;
        }
    }
}
