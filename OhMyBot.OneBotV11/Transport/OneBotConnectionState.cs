namespace OhMyBot.OneBotV11.Transport;

public enum OneBotConnectionState
{
    Stopped = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Faulted = 4,
    Disposed = 5
}
