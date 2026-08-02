using Grpc.Core;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Admin;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Qq;
using OhMyBot.Core.Infrastructure.Terminal;
using OhMyBot.Core.Infrastructure.UserProfiles;

namespace OhMyBot.Core.Infrastructure.Grpc;

public sealed class CommandRouterGrpcService(
    CommandExecutionService commandExecutionService,
    CallbackExecutionService callbackExecutionService,
    PlatformUserProfileService userProfileService,
    QqMenuStore qqMenuStore,
    QqMenuConverter qqMenuConverter,
    IServiceScopeFactory scopeFactory,
    InteractiveConsoleOutputQueue consoleOutputQueue,
    ILogger<CommandRouterGrpcService> logger) : CommandRouter.CommandRouterBase
{
    public override async Task<CommandResponse> ExecuteCommand(CommandRequest request, ServerCallContext context)
    {
        var response = await commandExecutionService.ExecuteAsync(request, context.CancellationToken);
        return request.Platform == BotPlatform.Qq
            ? await qqMenuConverter.ToQqAsync(response, request.ChatType, context.CancellationToken)
            : response;
    }

    public override Task<GetRoutesResponse> GetRoutes(GetRoutesRequest request, ServerCallContext context)
    {
        return commandExecutionService.GetRoutesAsync(request, context.CancellationToken);
    }

    public override Task<CommandResponse> ExecuteCallback(CallbackRequest request, ServerCallContext context)
    {
        return callbackExecutionService.ExecuteAsync(request, context.CancellationToken);
    }

    public override async Task<BindQqMenuResponse> BindQqMenu(BindQqMenuRequest request, ServerCallContext context)
    {
        var bound = await qqMenuStore.BindAsync(
            request.ChatId,
            request.MessageId,
            request.SenderId,
            request.ChatType,
            request.MenuToken,
            context.CancellationToken);
        return new BindQqMenuResponse { Bound = bound };
    }

    public override async Task<CommandResponse> ExecuteQqMenuSelection(QqMenuSelectionRequest request, ServerCallContext context)
    {
        // 解析序号（1-based）。非数字/越界 → 静默，避免误触发或刷屏。
        if (!int.TryParse(request.Selection.Trim(), out var choice) || choice < 1)
        {
            return SilentQq();
        }

        var replyTo = string.IsNullOrEmpty(request.ReplyToMessageId) ? null : request.ReplyToMessageId;
        var payload = await qqMenuStore.ResolveAsync(
            request.ChatId,
            replyTo,
            request.UserId,
            request.ChatType,
            choice - 1,
            context.CancellationToken);
        if (payload is null)
        {
            // 菜单已过期 / 选项越界 / 非菜单回复：静默忽略。
            return SilentQq();
        }

        var response = await callbackExecutionService.ExecuteAsync(new CallbackRequest
        {
            Platform = BotPlatform.Qq,
            BotInstanceId = request.BotInstanceId,
            ChatId = request.ChatId,
            UserId = request.UserId,
            MessageId = request.MessageId,
            ChatType = request.ChatType,
            Payload = payload
        }, context.CancellationToken);

        return await qqMenuConverter.ToQqAsync(response, request.ChatType, context.CancellationToken);
    }

    private static CommandResponse SilentQq() => new() { Qq = new QqResponse() };

    public override async Task<UserProfileResponse> RecordUserProfile(UserProfileRequest request, ServerCallContext context)
    {
        await userProfileService.RecordAsync(request, context.CancellationToken);
        return new UserProfileResponse { Recorded = true };
    }

    public override async Task OpenAdminConsole(
        IAsyncStreamReader<AdminConsoleInput> requestStream,
        IServerStreamWriter<AdminConsoleOutput> responseStream,
        ServerCallContext context)
    {
        using var stopLogs = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var writeGate = new SemaphoreSlim(1, 1);

        async Task WriteAsync(AdminConsoleOutput output, CancellationToken cancellationToken)
        {
            await writeGate.WaitAsync(cancellationToken);
            try
            {
                await responseStream.WriteAsync(output, cancellationToken);
            }
            finally
            {
                writeGate.Release();
            }
        }

        var logTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in consoleOutputQueue.ReadAllAsync(stopLogs.Token))
                {
                    await WriteAsync(ToOutput(item.Segments), stopLogs.Token);
                }
            }
            catch (OperationCanceledException) when (stopLogs.IsCancellationRequested)
            {
            }
        }, CancellationToken.None);

        await WriteAsync(ToOutput([new ConsoleTextSegment("Connected to OhMyBot Core. Type 'exit' to leave remote console.")]), context.CancellationToken);

        try
        {
            while (await requestStream.MoveNext(context.CancellationToken))
            {
                var commandLine = requestStream.Current.CommandLine.Trim();
                if (string.IsNullOrWhiteSpace(commandLine))
                {
                    continue;
                }

                if (IsExitCommand(commandLine))
                {
                    await WriteAsync(ToOutput([new ConsoleTextSegment("Leaving remote console.")]), context.CancellationToken);
                    break;
                }

                await ExecuteAdminCommandAsync(commandLine, WriteAsync, context.CancellationToken);
            }
        }
        catch (IOException)
        {
            // 控制台客户端非正常断开（关终端、Ctrl+C、ssh 掉线）会让请求流直接中断。
            // 这是预期情形而非故障，放任异常穿出去只会换来一屏无用堆栈。
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // 同上：连接已被取消，正常收尾即可。
        }
        finally
        {
            await stopLogs.CancelAsync();
            await logTask;
        }
    }

    private async Task ExecuteAdminCommandAsync(
        string commandLine,
        Func<AdminConsoleOutput, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var executor = scope.ServiceProvider.GetRequiredService<AdminCommandExecutor>();
            var result = await executor.ExecuteAsync(commandLine, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                var message = result.Success ? result.Message : $"Error: {result.Message}";
                await writeAsync(ToOutput([new ConsoleTextSegment(message)]), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to execute remote admin console command.");
            await writeAsync(ToOutput([new ConsoleTextSegment($"Error: {exception.Message}", ConsoleColor.Red)]), cancellationToken);
        }
    }

    private static AdminConsoleOutput ToOutput(IReadOnlyList<ConsoleTextSegment> segments)
    {
        var output = new AdminConsoleOutput();
        foreach (var segment in segments)
        {
            var grpcSegment = new AdminConsoleTextSegment { Text = segment.Text };
            if (segment.ForegroundColor is not null)
            {
                grpcSegment.ForegroundColor = (int)segment.ForegroundColor.Value;
            }

            if (segment.BackgroundColor is not null)
            {
                grpcSegment.BackgroundColor = (int)segment.BackgroundColor.Value;
            }

            output.Segments.Add(grpcSegment);
        }

        return output;
    }

    private static bool IsExitCommand(string commandLine)
    {
        return string.Equals(commandLine, "exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(commandLine, "quit", StringComparison.OrdinalIgnoreCase);
    }
}
