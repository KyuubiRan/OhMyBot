using System.Text.Json;
using OhMyOneBot.V11.Lib.Events.Messages.Group;
using OhMyOneBot.V11.Lib.Events.Messages.Private;
using OhMyOneBot.V11.Lib.Events.Meta;

namespace OhMyOneBot.V11.Lib.Events;

internal static class EventParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static EventBase? Parse(string rawEvent)
    {
        using var document = JsonDocument.Parse(rawEvent);
        var root = document.RootElement;

        if (!root.TryGetProperty("post_type", out var postTypeElement) || postTypeElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var postType = postTypeElement.GetString();
        return postType switch
        {
            "message" => ParseMessageEvent(root, rawEvent),
            "notice" => ParseNoticeEvent(root, rawEvent),
            "request" => ParseRequestEvent(root, rawEvent),
            "meta_event" => ParseMetaEvent(root, rawEvent),
            _ => null
        };
    }

    private static EventBase? ParseMessageEvent(JsonElement root, string rawEvent)
    {
        if (!root.TryGetProperty("message_type", out var messageTypeElement) || messageTypeElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return messageTypeElement.GetString() switch
        {
            "private" => Deserialize<PrivateMessageEvent>(rawEvent),
            "group" => Deserialize<GroupMessageEvent>(rawEvent),
            _ => Deserialize<EventBaseProxy>(rawEvent)
        };
    }

    private static EventBase? ParseNoticeEvent(JsonElement root, string rawEvent)
    {
        if (!root.TryGetProperty("notice_type", out var noticeTypeElement) || noticeTypeElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var noticeType = noticeTypeElement.GetString();
        return noticeType switch
        {
            "group_upload" => Deserialize<GroupFileUploadeEvent>(rawEvent),
            "group_admin" => Deserialize<GroupAdminChangeEvent>(rawEvent),
            "group_decrease" => Deserialize<GroupMemberDecrementEvent>(rawEvent),
            "group_increase" => Deserialize<GroupMemberIncrementEvent>(rawEvent),
            "group_ban" => Deserialize<GroupMuteEvent>(rawEvent),
            "friend_add" => Deserialize<FriendAddEvent>(rawEvent),
            "group_recall" => Deserialize<GroupMessageRevokeEvent>(rawEvent),
            "friend_recall" => Deserialize<PrivateMessageRevokeEvent>(rawEvent),
            "notify" => ParseNotifyEvent(root, rawEvent),
            _ => Deserialize<NoticeEventProxy>(rawEvent)
        };
    }

    private static EventBase? ParseNotifyEvent(JsonElement root, string rawEvent)
    {
        if (!root.TryGetProperty("sub_type", out var subTypeElement) || subTypeElement.ValueKind != JsonValueKind.String)
        {
            return Deserialize<GroupNoticeEvent>(rawEvent);
        }

        return subTypeElement.GetString() switch
        {
            "poke" => Deserialize<GroupPokeEvent>(rawEvent),
            "lucky_king" => Deserialize<GroupRedEnvelopeLuckyDogEvent>(rawEvent),
            "honor" => Deserialize<GroupMemberHonorChangeEvent>(rawEvent),
            _ => Deserialize<GroupNoticeEvent>(rawEvent)
        };
    }

    private static EventBase? ParseRequestEvent(JsonElement root, string rawEvent)
    {
        if (!root.TryGetProperty("request_type", out var requestTypeElement) || requestTypeElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return requestTypeElement.GetString() switch
        {
            "friend" => Deserialize<FriendAddRequestEvent>(rawEvent),
            "group" => Deserialize<GroupJoinRequestEvent>(rawEvent),
            _ => Deserialize<EventBaseProxy>(rawEvent)
        };
    }

    private static EventBase? ParseMetaEvent(JsonElement root, string rawEvent)
    {
        if (!root.TryGetProperty("meta_event_type", out var metaEventTypeElement) || metaEventTypeElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return metaEventTypeElement.GetString() switch
        {
            "lifecycle" => Deserialize<LifetimeEvent>(rawEvent),
            "heartbeat" => Deserialize<HeartBeatEvent>(rawEvent),
            _ => Deserialize<MetaEventProxy>(rawEvent)
        };
    }

    private static TEvent? Deserialize<TEvent>(string rawEvent) where TEvent : EventBase
    {
        return JsonSerializer.Deserialize<TEvent>(rawEvent, JsonOptions);
    }

    private sealed class EventBaseProxy : EventBase;

    private sealed class NoticeEventProxy : NoticeEvent;

    private sealed class MetaEventProxy : MetaEvent;
}
