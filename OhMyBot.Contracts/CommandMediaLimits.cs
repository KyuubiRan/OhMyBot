namespace OhMyBot.Contracts;

public static class CommandMediaLimits
{
    public const int MaxContentBytes = 20 * 1024 * 1024;

    public const int MaxGrpcMessageBytes = 24 * 1024 * 1024;
}
