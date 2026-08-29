namespace OhMyBot.Core.Commanding.Commands;

public interface ICommandProgressReporter
{
    bool HasReported { get; }

    Task ReportAsync(string message, CancellationToken cancellationToken = default);
}
