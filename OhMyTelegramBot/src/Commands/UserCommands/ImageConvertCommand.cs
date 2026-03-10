using System.Collections.Frozen;
using FoxTail.Extensions;
using Microsoft.Extensions.Logging;
using OhMyLib.Attributes;
using OhMyLib.Services;
using OhMyLib.Utils;
using OhMyTelegramBot.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.UserCommands;

[Component(Key = "cmd__imgcvt")]
public class ImageConvertCommand(CacheFileService cacheFileService, ILogger<ImageConvertCommand> logger) : ICommand
{
    private static readonly FrozenSet<string> AvailableFormats = new[]
    {
        "png",
        "jpg",
        "webp",
        "sticker"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        if (args.IsEmpty || message.ReplyToMessage is not { Photo: { IsNotEmpty: true } photos } ||
            photos.OrderByDescending(x => x.FileSize ?? 0)
                  .FirstOrDefault() is not { } photo)
        {
            await botClient.SendMessage(
                chatId, "用法（回复图片）：/imgcvt <格式> [参数]... \n支持格式：png, jpg, webp, sticker\n参数(可选)：w=[宽度] h=[高度] q=[质量，1-100，仅jpg/webp有效]");
            return;
        }

        var format = args[0].ToLowerInvariant();
        if (!AvailableFormats.Contains(format))
        {
            await botClient.SendMessage(chatId, "不支持的格式：" + format);
            return;
        }

        var msg = await botClient.SendMessage(chatId, "下载中...");

        var tgFileName = $"photo_{photo.FileId}";
        var srcFile = cacheFileService.MakeFileInfo(tgFileName, CacheFileService.OnConflict.Ignore);
        try
        {
            if (!srcFile.Exists)
            {
                await using var fs = srcFile.Create();
                await botClient.GetInfoAndDownloadFile(photo.FileId, fs);
            }
        }
        catch (Exception e)
        {
            await botClient.EditMessageText(msg.Chat.Id, msg.Id, "下载图片时发生错误：" + e.Message);
            logger.LogWarning(e, "Download photo failed: {FileId}", photo.FileId);
            return;
        }

        int w = photo.Width, h = photo.Height, q = 100;
        foreach (var arg in args[1..])
        {
            var parts = arg.Split('=', 2);
            if (parts.Length != 2)
                continue;

            var k = parts[0].ToLowerInvariant();
            var v = parts[1];

            switch (k)
            {
                case "w" or "width" when int.TryParse(v, out var width):
                    w = width;
                    break;
                case "h" or "height" when int.TryParse(v, out var height):
                    h = height;
                    break;
                case "q" or "quality" when int.TryParse(v, out var quality):
                    q = int.Clamp(quality, 1, 100);
                    break;
            }
        }

        var isSticker = format == "sticker";
        if (isSticker)
        {
            var max = Math.Max(w, h);
            var scale = 512.0 / max;

            w = (int)Math.Round(w * scale);
            h = (int)Math.Round(h * scale);
        }

        var targetFile = cacheFileService.MakeFileInfo($"imgcvt_{photo.FileId}_{format}_w{w}_h{h}_q{q}.{(isSticker ? "webp" : format)}",
                                                       CacheFileService.OnConflict.Overwrite);

        await botClient.EditMessageText(msg.Chat.Id, msg.Id, "转换中...");

        try
        {
            if (!targetFile.Exists)
            {
                await using var read = srcFile.OpenRead();
                await using var write = targetFile.Create();
                switch (format)
                {
                    case "png":
                        ImageConverter.ToPng(read, write, new ResizeOptions
                        {
                            Size = new Size(w, h),
                            Mode = ResizeMode.Max
                        });
                        break;
                    case "jpg":
                        ImageConverter.ToJpeg(read, write, new ResizeOptions
                        {
                            Size = new Size(w, h),
                            Mode = ResizeMode.Max
                        }, q);
                        break;
                    case "webp":
                    case "sticker":
                        ImageConverter.ToWebp(read, write, new ResizeOptions
                        {
                            Size = new Size(w, h),
                            Mode = ResizeMode.Max,
                        }, q);
                        break;
                }
            }
        }
        catch (Exception e)
        {
            await botClient.EditMessageText(msg.Chat.Id, msg.Id, "转换图片时发生错误：" + e.Message);
            logger.LogWarning(e, "Convert image failed: {FileId}, format: {Format}, w: {Width}, h: {Height}, q: {Quality}",
                              photo.FileId, format, w, h, q);
            return;
        }

        await botClient.EditMessageText(msg.Chat.Id, msg.Id, "上传中...");

        await using var targetRead = targetFile.OpenRead();
        await botClient.SendDocument(chatId, targetRead);

        await botClient.DeleteMessage(msg.Chat.Id, msg.Id);
    }
}