using OhMyBot.Contracts.Grpc;
using Telegram.Bot.Extensions;

namespace OhMyBot.TelegramGateway.Rendering;

public sealed class HelpTelegramRenderer : ITelegramCommandResultRenderer
{
    public bool CanRender(CommandResponse response)
    {
        return response.Code == 0
            && response.DataKind == CommandResponseDataKind.Text
            && IsHelpText(response.Text.Text);
    }

    public IReadOnlyList<TelegramOutgoingMessage> Render(CommandResponse response)
    {
        return string.IsNullOrWhiteSpace(response.Text.Text)
            ? []
            : [TelegramTextMessage.Markdown(RenderHelp(response.Text.Text))];
    }

    private static bool IsHelpText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(line => line.Contains(" - ", StringComparison.Ordinal));
    }

    private static string RenderHelp(string text)
    {
        return string.Join('\n', text
            .Split('\n')
            .Select(RenderLine));
    }

    private static string RenderLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var separatorIndex = line.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return Markdown.Escape(line);
        }

        var name = line[..separatorIndex];
        var description = line[(separatorIndex + 3)..];
        var renderedName = name.StartsWith("/", StringComparison.Ordinal)
            ? Markdown.Escape(name)
            : Code(name);
        return $"{renderedName} \\- {RenderDescription(description)}";
    }

    private static string RenderDescription(string value)
    {
        var parts = value.Split('`');
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = i % 2 == 0
                ? Markdown.Escape(parts[i])
                : Code(parts[i]);
        }

        return string.Concat(parts);
    }

    private static string Code(string value)
    {
        return $"`{Markdown.Escape(value)}`";
    }
}
