using System.Text;
using FoxTail.Extensions;
using Microsoft.Extensions.Logging;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Enums.Kuro;
using OhMyLib.Requests.Kuro;
using OhMyLib.Requests.Kuro.Data;
using OhMyLib.Services;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.UserCommands.Kuro;

[Component(Key = "cmd__kuro_signin")]
public class KuroBbsSignInCommand(BotUserService userService, ILogger<KuroBbsSignInCommand> logger) : ICommand
{
    private static bool ShouldDoAction(KuroBbsTaskType tasks, KuroBbsTaskType target, string[] args, string key) =>
        (tasks & target) != 0 || args.IsEmpty || args.Any(x => x.Equals(key, StringComparison.CurrentCultureIgnoreCase));

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var botUser = await userService.GetUserAsync(senderId.ToString(), SoftwareType.Telegram);
        var kUser = botUser?.KuroUser;
        if (kUser == null)
        {
            await botClient.SendMessage(chatId, "请先绑定库街区账号后再使用签到功能");
            return;
        }

        if (kUser.Token.IsWhiteSpaceOrNull)
        {
            await botClient.SendMessage(chatId, "绑定的库街区账号信息不完整，请重新绑定后再使用签到功能");
            return;
        }

        var tasks = kUser.BbsTask;

        var msg = await botClient.SendMessage(chatId, "签到中，请稍候...");

        var resultMessage = new StringBuilder("签到结果：\n");
        _ = Task.Run(async () =>
        {
            using var client = new KuroHttpClient(kUser.Token, kUser.DevCode, kUser.DistinctId, kUser.IpAddress);

            var taskProgress = await client.BbsGetTaskProgressAsync(kUser.BbsUserId ?? 0);
            if (!taskProgress.Success)
            {
                throw new InvalidOperationException("获取任务进度失败：" + taskProgress.Msg);
            }

            var currentProgress = taskProgress.Data?.DailyTask ?? [];
            var sb = new StringBuilder("签到中，请稍候...\n");
            sb.AppendLine("当前任务进度：");
            foreach (var task in currentProgress)
            {
                sb.AppendLine($"- {task.Remark}: {task.CompleteTimes}/{task.NeedActionTimes} (+{task.GainGold})");
            }

            await botClient.EditMessageText(chatId, msg.MessageId, sb.ToString());

            var signTask = currentProgress.FirstOrDefault(x => x.Remark == "用户签到");
            if (signTask?.Finished == false && ShouldDoAction(tasks, KuroBbsTaskType.Signin, args, "signin"))
            {
                var result = await client.BbsSignInAsync();
                if (result.Success)
                {
                    logger.LogInformation("User {UserId} signed in to Kuro BBS successfully.", senderId);
                    resultMessage.AppendLine("社区签到成功");
                }
                else
                {
                    logger.LogWarning("User {UserId} signed in to Kuro BBS failed, message: {Message}", senderId, result.Msg);
                    resultMessage.AppendLine($"社区签到失败！消息：{result.Msg}");
                }

                await Task.Delay(Random.Shared.Next(1000, 2000));
            }

            KuroHttpResponse<KuroBbsPostData>? posts = null;

            var viewTask = currentProgress.FirstOrDefault(x => x.Remark == "浏览3篇帖子");
            if (viewTask?.Finished == false && ShouldDoAction(tasks, KuroBbsTaskType.ViewPosts, args, "view"))
            {
                posts ??= await client.BbsGetPostsAsync();

                var pst = posts.Data?.PostList ?? [];

                int succCnt = 0, failedCnt = 0;
                for (var i = viewTask.CompleteTimes; i < viewTask.NeedActionTimes; i++)
                {
                    if (pst.IsEmpty)
                        break;

                    var post = pst[i % pst.Count];
                    var viewResult = await client.BbsGetPostDetailAsync(post.PostId);
                    if (viewResult.Success)
                    {
                        logger.LogInformation("User {UserId} viewed post {PostId} successfully.", senderId, post.PostId);
                        succCnt++;
                    }
                    else
                    {
                        logger.LogWarning("User {UserId} viewed post {PostId} failed, message: {Message}", senderId, post.PostId, viewResult.Msg);
                        failedCnt++;
                    }

                    await Task.Delay(Random.Shared.Next(1000, 2000));
                }

                resultMessage.AppendLine($"浏览帖子任务完成，成功：{succCnt}，失败：{failedCnt}");
            }

            var likeTask = currentProgress.FirstOrDefault(x => x.Remark == "点赞5次");
            if (likeTask?.Finished == false && ShouldDoAction(tasks, KuroBbsTaskType.LikePosts, args, "like"))
            {
                posts ??= await client.BbsGetPostsAsync();
                var pst = posts.Data?.PostList ?? [];

                int succCnt = 0, failedCnt = 0;

                for (var i = likeTask.CompleteTimes; i < likeTask.NeedActionTimes; i++)
                {
                    if (pst.IsEmpty)
                        break;

                    var post = pst[i % pst.Count];
                    var likeResult = await client.BbsLikePostAsync(
                        post.GameId,
                        post.GameForumId,
                        post.PostType,
                        post.PostId,
                        post.UserId
                    );

                    if (likeResult.Success)
                    {
                        logger.LogInformation("User {UserId} liked post {PostId} successfully.", senderId, post.PostId);
                        succCnt++;
                    }
                    else
                    {
                        logger.LogWarning("User {UserId} liked post {PostId} failed, message: {Message}", senderId, post.PostId, likeResult.Msg);
                        failedCnt++;
                    }

                    await Task.Delay(Random.Shared.Next(1000, 2000));
                }

                resultMessage.AppendLine($"点赞帖子任务完成，成功：{succCnt}，失败：{failedCnt}");
            }

            var shareTask = currentProgress.FirstOrDefault(x => x.Remark == "分享1次帖子");
            if (shareTask?.Finished == false && ShouldDoAction(tasks, KuroBbsTaskType.SharePosts, args, "share"))
            {
                var shareResult = await client.BbsSharePostAsync();
                if (shareResult.Success)
                {
                    logger.LogInformation("User {UserId} shared post successfully.", senderId);
                    resultMessage.AppendLine("分享帖子任务完成，结果：成功");
                }
                else
                {
                    logger.LogWarning("User {UserId} shared post failed, message: {Message}", senderId, shareResult.Msg);
                    resultMessage.AppendLine("分享帖子任务完成，结果：失败，消息：" + shareResult.Msg);
                }
            }
        }).ContinueWith(async t =>
        {
            if (t.IsFaulted)
            {
                await botClient.EditMessageText(chatId, msg.MessageId, "签到过程中出现错误：" + t.Exception?.GetBaseException().Message);
            }
            else
            {
                await botClient.EditMessageText(chatId, msg.MessageId, resultMessage.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}").ToString().Trim());
            }
        });
    }
}