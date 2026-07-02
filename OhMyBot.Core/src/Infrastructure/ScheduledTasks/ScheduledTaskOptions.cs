namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public sealed class ScheduledTaskOptions
{
    public bool Enabled { get; set; } = true;

    public string Cron { get; set; } = "10 0 * * *";
}
