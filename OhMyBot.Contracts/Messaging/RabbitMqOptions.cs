namespace OhMyBot.Contracts.Messaging;

public sealed class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    public string NotificationExchange { get; set; } = "ohmybot.notifications";

    public string NotificationQueue { get; set; } = string.Empty;

    public string NotificationRoutingKey { get; set; } = string.Empty;
}
