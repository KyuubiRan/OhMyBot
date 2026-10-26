using System.Net;
using Microsoft.Extensions.Logging;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Infrastructure.ScheduledTasks;

namespace OhMyBot.Plugin.Commanding.SignIn;

public sealed record SignInModule(
    string Name,
    Func<CancellationToken, Task<IReadOnlyList<string>>> ExecuteAsync);

/// <summary>
/// 签到模块共享的失败隔离与请求重试。模块顺序执行，避免并发使用同一个账号的 DbContext。
/// 重试应包住单个请求，或先查询签到状态的单角色操作，不能重跑已完成的其他模块。
/// </summary>
public sealed class SignInExecutor
{
    public const int DefaultMaxRetries = 3;
    private readonly ILogger<SignInExecutor> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retryDelay;
    private readonly int _maxRetries;

    public SignInExecutor(
        ILogger<SignInExecutor> logger,
        TimeProvider? timeProvider = null,
        TimeSpan? retryDelay = null,
        int maxRetries = DefaultMaxRetries)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(1);
        if (_retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxRetries = maxRetries;
    }

    public async Task<T> ExecuteWithRetryAsync<T>(
        string operation,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt > _maxRetries)
                {
                    throw new SignInRetryExhaustedException(operation, attempt, exception);
                }

                _logger.LogWarning(exception,
                    "Sign-in request failed; retrying. operation={Operation}, retry={Retry}/{MaxRetries}.",
                    operation, attempt, _maxRetries);
                await Task.Delay(_retryDelay * attempt, _timeProvider, cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyList<string>> RunModuleAsync(
        string name,
        Func<CancellationToken, Task<IReadOnlyList<string>>> action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await action(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var errorId = Guid.NewGuid().ToString("N")[..6];
            ManagedTaskExecutionContext.ReportFailure(exception, $"模块：{name}，错误 id: {errorId}");
            _logger.LogWarning(exception,
                "Sign-in module failed. module={Module}, errorId={ErrorId}.", name, errorId);
            return [$"{name}执行失败：{DescribeFailure(exception)}（错误 id: {errorId}）"];
        }
    }

    public async Task<IReadOnlyList<string>> RunModulesAsync(
        IEnumerable<SignInModule> modules,
        CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        foreach (var module in modules)
        {
            lines.AddRange(await RunModuleAsync(module.Name, module.ExecuteAsync, cancellationToken));
        }

        return lines;
    }

    public static string DescribeFailure(Exception exception)
    {
        return exception switch
        {
            SignInRetryExhaustedException exhausted
                => $"{DescribeFailure(exhausted.InnerException!)}（已重试 {exhausted.Attempts - 1} 次）",
            CommandUserException userException => userException.Message,
            SignInTransientException transient => transient.Message,
            TimeoutException or OperationCanceledException { InnerException: TimeoutException }
                => "接口请求超时，请稍后手动重试。",
            OperationCanceledException => "接口请求被中断，请稍后手动重试。",
            HttpRequestException => "接口请求失败，请稍后手动重试。",
            _ => "签到处理异常，请稍后重试。"
        };
    }

    private static bool IsTransient(Exception exception)
    {
        return exception switch
        {
            SignInTransientException or TimeoutException or OperationCanceledException => true,
            HttpRequestException { StatusCode: null } => true,
            HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests } => true,
            HttpRequestException { StatusCode: { } status } => (int)status >= 500,
            _ => false
        };
    }
}

/// <summary>插件明确识别出的临时接口错误，消息应为可直接展示的业务文案。</summary>
public sealed class SignInTransientException(string userMessage) : Exception(userMessage);

public sealed class SignInRetryExhaustedException(string operation, int attempts, Exception innerException)
    : Exception($"Sign-in operation '{operation}' failed after {attempts} attempts.", innerException)
{
    public string Operation { get; } = operation;
    public int Attempts { get; } = attempts;
}
