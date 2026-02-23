namespace OkieRummyGodot.Core.Domain;

public enum TileColor
{
    Red,
    Blue,
    Black,
    Yellow
}

public enum TurnPhase
{
    Waiting,
    Draw,
    Discard
}

public enum GameStatus
{
    Menu,
    Playing,
    Victory,
    GameOver
}

public enum PlayerConnectionState
{
    CONNECTED,
    TEMP_DISCONNECTED,
    RECONNECTING,
    RECONNECTED,
    LEFT_INTENTIONALLY,
    TIMED_OUT,
    REPLACED_BY_BOT,
    FORFEITED,
    GAME_OVER
}

public enum GamePhase
{
    Lobby,
    Dealing,
    Playing,
    Finished
}
