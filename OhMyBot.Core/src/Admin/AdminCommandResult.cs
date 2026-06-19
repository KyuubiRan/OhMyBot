namespace OhMyBot.Core.Admin;

public sealed record AdminCommandResult(bool Success, string Message)
{
    public static AdminCommandResult Ok(string message)
    {
        return new AdminCommandResult(true, message);
    }

    public static AdminCommandResult Error(string message)
    {
        return new AdminCommandResult(false, message);
    }
}
