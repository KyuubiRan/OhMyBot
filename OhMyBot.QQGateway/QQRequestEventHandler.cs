using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts;
using OhMyBot.Contracts.Grpc;
using OhMyBot.OneBotV11;
using OhMyBot.OneBotV11.Events;
using OhMyBot.OneBotV11.Events.Messages.Group;
using OhMyBot.OneBotV11.Events.Messages.Private;
using OhMyBot.OneBotV11.Transport;

namespace OhMyBot.QQGateway;

// 订阅 OneBot 的 request 事件（加好友 / 邀请入群 / 入群申请），上报给 Core 由插件决定怎么审批。
// 网关不做任何过滤和判断，只把 OneBot 的 flag 原样带上，并尽力补齐申请人档案（QQ 的 request
// 事件只带 user_id，没有昵称/等级）；审批结果经 QQRequestDecisionConsumerService 回到本进程
// 再翻译成 OneBot 动作。
public sealed class QQRequestEventHandler(
    QQCommandGateway gateway,
    IOneBotClient oneBotClient,
    IOptions<QQGatewayOptions> options,
    ILogger<QQRequestEventHandler> logger)
{
    private readonly QQGatewayOptions _options = options.Value;

    // OnEvent 在接收循环线程上同步触发，这里立即卸载到后台任务，避免阻塞收包。
    public void Handle(EventBase evt)
    {
        var report = evt switch
        {
            FriendAddRequestEvent friend => new PlatformRequestReport
            {
                Kind = PlatformRequestKind.FriendAdd,
                Flag = friend.Flag,
                RequesterId = friend.RequesterId.ToString(),
                Comment = friend.Comment,
                OccurredAt = friend.Timestamp
            },
            GroupJoinRequestEvent group when group.IsInvite || group.IsAdd => new PlatformRequestReport
            {
                Kind = group.IsInvite ? PlatformRequestKind.GroupInvite : PlatformRequestKind.GroupAdd,
                Flag = group.Flag,
                RequesterId = group.RequesterId.ToString(),
                GroupId = group.GroupId.ToString(),
                Comment = group.Comment,
                OccurredAt = group.Timestamp
            },
            _ => null
        };

        if (report is null)
        {
            return;
        }

        report.Platform = BotPlatform.Qq;
        report.BotInstanceId = _options.BotInstanceId;

        _ = Task.Run(async () =>
        {
            try
            {
                await FillRequesterProfileAsync(report);
                await FillGroupNameAsync(report);
                var accepted = await gateway.ReportPlatformRequestAsync(report);
                logger.LogInformation(
                    "已上报 QQ 待审批请求。kind={Kind} requester={Requester} group={Group} accepted={Accepted}",
                    report.Kind,
                    report.RequesterId,
                    report.GroupId,
                    accepted);
            }
            catch (Exception exception)
            {
                // 上报失败只记日志：请求仍留在 QQ 客户端里，owner 可手动处理。
                logger.LogError(
                    exception,
                    "上报 QQ 待审批请求失败。kind={Kind} requester={Requester}",
                    report.Kind,
                    report.RequesterId);
            }
        });
    }

    // 用 get_stranger_info 补昵称/性别/年龄/等级（对方不是好友也能查），头像按 QQ 的固定 URL 规则拼。
    // 查不到就只带 QQ 号上报：档案是锦上添花，不能因为它让整条请求丢掉。
    private async Task FillRequesterProfileAsync(PlatformRequestReport report)
    {
        if (!long.TryParse(report.RequesterId, out var userId))
        {
            return;
        }

        report.RequesterProfile[PlatformRequestProfileKeys.AvatarUrl] =
            $"https://q.qlogo.cn/headimg_dl?dst_uin={userId}&spec=640";

        try
        {
            var response = await oneBotClient.SendActionAsync(
                new OneBotActionRequest("get_stranger_info", new { user_id = userId, no_cache = true }));
            if (!response.IsSuccess || response.Data.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning(
                    "get_stranger_info 未返回档案，只带 QQ 号上报。requester={Requester} retcode={RetCode}",
                    report.RequesterId,
                    response.RetCode);
                return;
            }

            var data = response.Data;
            var nickname = ReadString(data, "nickname");
            if (!string.IsNullOrWhiteSpace(nickname))
            {
                report.RequesterName = nickname;
                report.RequesterProfile[PlatformRequestProfileKeys.Nickname] = nickname;
            }

            Put(report, PlatformRequestProfileKeys.Gender, ReadString(data, "sex"));
            Put(report, PlatformRequestProfileKeys.Age, ReadString(data, "age"));
            // NapCat 用 qqLevel，OneBot 标准实现用 level；两个都试。
            Put(report, PlatformRequestProfileKeys.Level, ReadString(data, "qqLevel") ?? ReadString(data, "level"));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "get_stranger_info 查询失败。requester={Requester}", report.RequesterId);
        }
    }

    // 群名对判断「这是哪个群」比群号有用得多，尤其是邀请入群。
    // 机器人还没进群时 get_group_info 也能查到公开群资料；查不到就退回只显示群号。
    private async Task FillGroupNameAsync(PlatformRequestReport report)
    {
        if (!long.TryParse(report.GroupId, out var groupId))
        {
            return;
        }

        try
        {
            var response = await oneBotClient.SendActionAsync(
                new OneBotActionRequest("get_group_info", new { group_id = groupId, no_cache = true }));
            if (!response.IsSuccess || response.Data.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning(
                    "get_group_info 未返回群资料，只带群号上报。group={Group} retcode={RetCode}",
                    report.GroupId,
                    response.RetCode);
                return;
            }

            Put(report, PlatformRequestProfileKeys.GroupName, ReadString(response.Data, "group_name"));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "get_group_info 查询失败。group={Group}", report.GroupId);
        }
    }

    private static void Put(PlatformRequestReport report, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value != "0" && value != "unknown")
        {
            report.RequesterProfile[key] = value;
        }
    }

    private static string? ReadString(JsonElement data, string property)
    {
        if (!data.TryGetProperty(property, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };
    }
}
