using OhMyBot.Core.ScheduledTasks;

namespace OhMyBot.Core.Admin;

public sealed class TaskCtlAdminCommand(ManagedTaskRegistry registry) : IAdminCommand
{
    private static readonly AdminCommandDefinition CommandDefinition = new(
        "taskctl",
        "taskctl <list|status|enable|disable|execute|cancel> [task]",
        "Manage Core scheduled tasks.",
        [],
        [],
        [
            "taskctl list",
            "taskctl status ai-router-auto-sign",
            "taskctl execute ai-router-auto-sign"
        ]);

    public AdminCommandDefinition Definition => CommandDefinition;

    public async Task<AdminCommandResult> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            return AdminCommandResult.Error("Usage: " + Definition.Usage);
        }

        var verb = args[0].Trim().ToLowerInvariant();
        if (verb == "list")
        {
            return AdminCommandResult.Ok(string.Join(Environment.NewLine, registry.Tasks.Select(FormatTask)));
        }

        if (args.Count < 2)
        {
            return AdminCommandResult.Error("Task name is required.");
        }

        if (!registry.TryGet(args[1], out var task))
        {
            return AdminCommandResult.Error($"Unknown task: {args[1]}.");
        }

        switch (verb)
        {
            case "status":
                return AdminCommandResult.Ok(FormatTask(task));
            case "enable":
                task.Enabled = true;
                return AdminCommandResult.Ok($"Enabled task: {task.Name}.");
            case "disable":
                task.Enabled = false;
                return AdminCommandResult.Ok($"Disabled task: {task.Name}.");
            case "cancel":
                return AdminCommandResult.Ok(task.Cancel() ? $"Cancel requested: {task.Name}." : $"Task is not running: {task.Name}.");
            case "execute":
                _ = Task.Run(() => task.ExecuteAsync(cancellationToken), CancellationToken.None);
                return AdminCommandResult.Ok($"Execution started: {task.Name}.");
            default:
                return AdminCommandResult.Error("Usage: " + Definition.Usage);
        }
    }

    private static string FormatTask(IManagedTask task)
    {
        return string.Join(Environment.NewLine,
            $"Name: {task.Name}",
            $"Description: {task.Description}",
            $"Enabled: {task.Enabled}",
            $"Cron: {task.Cron}",
            $"Running: {task.IsRunning}",
            $"LastStartedAt: {task.LastStartedAt?.ToString("O") ?? "-"}",
            $"LastCompletedAt: {task.LastCompletedAt?.ToString("O") ?? "-"}",
            $"LastError: {task.LastError ?? "-"}");
    }
}
