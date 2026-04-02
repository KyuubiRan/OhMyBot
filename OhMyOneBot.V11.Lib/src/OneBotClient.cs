namespace OhMyOneBot.V11.Lib;

public class OneBotClient : IOneBotClient
{
    private OneBotClient()
    {
    }

    public static OneBotClient CreateHttpClient()
    {
        return new OneBotClient();
    }

    public static OneBotClient CreateWebsocketClient()
    {
        return new OneBotClient();
    }

    public static OneBotClient CreateReversedWebsocketClient()
    {
        return new OneBotClient();
    }
}