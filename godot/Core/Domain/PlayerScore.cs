using System;

namespace OkieRummyGodot.Core.Domain;

public struct PlayerScore
{
    public string PlayerId { get; set; }
    public string PlayerName { get; set; }
    public int Score { get; set; }
    public bool IsWinner { get; set; }
    public string AvatarUrl { get; set; }
}
