namespace OhMyTelegramBot.Configs;

public class BotConfig
{
    public string Token { get; set; } = string.Empty;
    public long OwnerId { get; set; }
    public string[] CommandPrefixes { get; set; } = ["/"];
    public bool EnableProxy { get; set; } = false;
    public HttpProxyConfig HttpProxy { get; set; } = new();

    public class HttpProxyConfig
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 7890;
    }
}