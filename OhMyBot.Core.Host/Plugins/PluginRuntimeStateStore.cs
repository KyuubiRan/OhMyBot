using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OhMyBot.Core.Host.Plugins;

internal interface IPluginRuntimeStateStore
{
    IReadOnlyCollection<string>? LoadDisabledPluginIds();

    Task SaveDisabledPluginIdsAsync(
        IReadOnlyCollection<string> disabledPluginIds,
        CancellationToken cancellationToken = default);
}

internal sealed class PluginRuntimeStateStore : IPluginRuntimeStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _statePath;

    public PluginRuntimeStateStore(IOptions<PluginRuntimeOptions> options)
        : this(ResolveStatePath(options.Value.StatePath))
    {
    }

    internal PluginRuntimeStateStore(string statePath)
    {
        _statePath = Path.GetFullPath(statePath);
    }

    public IReadOnlyCollection<string>? LoadDisabledPluginIds()
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        using var stream = File.OpenRead(_statePath);
        var payload = JsonSerializer.Deserialize<PluginRuntimeStateDocument>(stream, SerializerOptions)
                      ?? throw new InvalidDataException($"插件运行时状态文件为空：{_statePath}");
        return payload.PluginRuntime?.DisabledPluginIds ?? [];
    }

    public async Task SaveDisabledPluginIdsAsync(
        IReadOnlyCollection<string> disabledPluginIds,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_statePath)
                        ?? throw new InvalidOperationException("插件运行时状态路径没有父目录。");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_statePath)}.{Guid.NewGuid():N}.tmp");
        var payload = new
        {
            PluginRuntime = new
            {
                DisabledPluginIds = disabledPluginIds
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            }
        };

        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    payload,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, _statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string ResolveStatePath(string statePath)
    {
        var path = string.IsNullOrWhiteSpace(statePath)
            ? "plugin-runtime-state.json"
            : statePath.Trim();
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
    }

    private sealed class PluginRuntimeStateDocument
    {
        public PluginRuntimeStateSection? PluginRuntime { get; init; }
    }

    private sealed class PluginRuntimeStateSection
    {
        public string[] DisabledPluginIds { get; init; } = [];
    }
}
