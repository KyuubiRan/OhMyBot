using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Routing;

namespace OhMyBot.Core.Commands;

public sealed class PlatformCommandDslExecutor(RouteStore routeStore)
{
    public async Task<CommandResponse> ExecuteAsync(CommandContext context)
    {
        var command = CommandDsl.Normalize(context.Request.Command);
        if (string.Equals(command, "help", StringComparison.OrdinalIgnoreCase))
        {
            return RenderHelp(context, context.Request.Args.Select(CommandDsl.Normalize).Where(arg => arg.Length > 0).ToArray());
        }

        var path = new[] { command }
            .Concat(context.Request.Args.Select(CommandDsl.Normalize).Where(arg => arg.Length > 0))
            .ToArray();

        if (!TryResolvePath(path, out var node, out var consumed))
        {
            return CommandResponses.Error("RouteNotFound", "Unknown route.", context);
        }

        var remainingArgs = context.Request.Args.Skip(consumed - 1).ToArray();
        if (!CanUseCurrent(context, node))
        {
            return CommandResponses.Error("PrivilegeDenied", "Insufficient privilege.", context);
        }

        if (node.Children.Count > 0 && remainingArgs.Length == 0)
        {
            return RenderHelp(context, path.Take(consumed).ToArray());
        }

        if (node.Children.Count > 0 && remainingArgs.Length > 0)
        {
            return CommandResponses.Error("RouteNotFound", "Unknown route.", context);
        }

        if (node.Handler is null)
        {
            return CommandResponses.Error("RouteTargetMissing", "The route target command is not available.", context);
        }

        var executableContext = context with { Request = CloneRequest(context.Request, node.Name, remainingArgs) };
        return await node.Handler(executableContext);
    }

    public CommandResponse RenderHelp(CommandContext context, IReadOnlyList<string> path)
    {
        var nodes = GetVisibleNodes(context, path, out var found);
        if (!found)
        {
            var name = path.Count == 0 ? string.Empty : string.Join(' ', path);
            return CommandResponses.Text($"未找到子命令：{name}", context);
        }

        var text = string.Join('\n', nodes.Select(node => RenderHelpLine(node, path.Count == 0)));
        return CommandResponses.Text(text, context);
    }

    private bool TryResolvePath(IReadOnlyList<string> path, out CommandDslNode node, out int consumed)
    {
        node = null!;
        consumed = 0;
        var currentNodes = routeStore.GetNodes();
        CommandDslNode? current = null;

        for (var i = 0; i < path.Count; i++)
        {
            var segment = path[i];
            var next = currentNodes.FirstOrDefault(candidate => string.Equals(candidate.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                break;
            }

            current = next;
            currentNodes = next.Children;
            consumed = i + 1;
        }

        if (current is null)
        {
            return false;
        }

        node = current;
        return true;
    }

    private IReadOnlyList<CommandDslNode> GetVisibleNodes(
        CommandContext context,
        IReadOnlyList<string> path,
        out bool found)
    {
        found = true;
        IReadOnlyList<CommandDslNode> nodes;
        if (path.Count == 0)
        {
            nodes = routeStore.GetNodes();
        }
        else if (routeStore.TryGetNode(path, out var node))
        {
            nodes = node.Children;
        }
        else
        {
            found = false;
            return [];
        }

        var platform = CommandDsl.ToSupportedPlatform(context.Request.Platform);
        var chatType = CommandDsl.ToSupportedChatType(context.Request.ChatType);
        return nodes
            .Where(node => CanUse(node, context.Identity.Privilege, platform, chatType))
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool CanUseCurrent(CommandContext context, CommandDslNode node)
    {
        return CanUse(
            node,
            context.Identity.Privilege,
            CommandDsl.ToSupportedPlatform(context.Request.Platform),
            CommandDsl.ToSupportedChatType(context.Request.ChatType));
    }

    private static string RenderHelpLine(CommandDslNode node, bool root)
    {
        var commandName = root ? "/" + node.Name : node.Name;
        return $"{commandName} - {node.Description}";
    }

    public static bool CanUse(
        CommandDslNode node,
        UserPrivilege privilege,
        SupportedPlatforms platform,
        SupportedChatTypes chatType)
    {
        return node.Enabled
            && platform is not SupportedPlatforms.None
            && chatType is not SupportedChatTypes.None
            && node.SupportPlatforms.HasFlag(platform)
            && node.SupportChatTypes.HasFlag(chatType)
            && (int)privilege >= (int)node.RequiredPrivilege;
    }

    private static CommandRequest CloneRequest(CommandRequest request, string command, IReadOnlyList<string> args)
    {
        var clone = request.Clone();
        clone.Command = command;
        clone.Args.Clear();
        clone.Args.AddRange(args);
        return clone;
    }
}
