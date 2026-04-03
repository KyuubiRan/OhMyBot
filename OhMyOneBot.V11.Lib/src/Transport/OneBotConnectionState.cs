namespace OhMyOneBot.V11.Lib.Transport;

public enum OneBotConnectionState
{
    Stopped = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Faulted = 4,
    Disposed = 5
}
