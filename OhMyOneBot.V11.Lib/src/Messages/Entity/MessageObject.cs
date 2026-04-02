using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Messages.Entity;

public abstract class MessageObject<T> where T : MessageObject<T>, new()
{
    [JsonPropertyName("type")] public string Type { get; init; } = null!;
    [JsonPropertyName("data")] public Dictionary<string, string> Parameters { get; init; } = [];

    public T WithParameter(string key, object? value)
    {
        var v = value switch
        {
            string s => string.IsNullOrWhiteSpace(s) ? null : s,
            bool b => b ? "1" : "0",
            List<T> entities => string.Join("", entities.Select(e => e.ToString())),
            _ => value?.ToString()
        };

        if (v != null)
            Parameters[key] = v;

        return (T)this;
    }

    /// <summary>
    /// 文本
    /// </summary>
    /// <param name="text">内容</param>
    /// <returns></returns>
    public static T Text(string text) => new() { Type = "text", Parameters = { ["text"] = text } };

    /// <summary>
    /// QQ 小表情
    /// </summary>
    /// <param name="id">QQ 表情 ID</param>
    /// <returns></returns>
    public static T Face(string id) => new() { Type = "face", Parameters = { ["id"] = id } };

    /// <summary>
    /// 图片
    /// </summary>
    /// <param name="file">图片文件名、绝对路径或 URL</param>
    /// <param name="type">图片类型，flash 表示闪照</param>
    /// <param name="url">图片 URL</param>
    /// <param name="cache">只在通过网络 URL 发送时有效，是否使用已缓存的文件</param>
    /// <param name="proxy">只在通过网络 URL 发送时有效，是否通过代理下载文件</param>
    /// <param name="timeout">只在通过网络 URL 发送时有效，下载超时时间（秒）</param>
    /// <returns></returns>
    public static T Image(string? file = null, string? type = null, string? url = null, bool? cache = null, bool? proxy = null, int? timeout = null) =>
        new T { Type = "image" }
            .WithParameter(nameof(file), file)
            .WithParameter(nameof(type), type)
            .WithParameter(nameof(url), url)
            .WithParameter(nameof(cache), cache)
            .WithParameter(nameof(proxy), proxy)
            .WithParameter(nameof(timeout), timeout);

    /// <summary>
    /// 语音
    /// </summary>
    /// <param name="file">语音文件名、绝对路径或 URL</param>
    /// <param name="magic">是否为变声</param>
    /// <param name="url">语音 URL</param>
    /// <param name="cache">只在通过网络 URL 发送时有效，是否使用已缓存的文件</param>
    /// <param name="proxy">只在通过网络 URL 发送时有效，是否通过代理下载文件</param>
    /// <param name="timeout">只在通过网络 URL 发送时有效，下载超时时间（秒）</param>
    /// <returns></returns>
    public static T Record(string? file = null, bool? magic = null, string? url = null, bool? cache = null, bool? proxy = null, int? timeout = null) =>
        new T { Type = "record" }
            .WithParameter(nameof(file), file)
            .WithParameter(nameof(magic), magic)
            .WithParameter(nameof(url), url)
            .WithParameter(nameof(cache), cache)
            .WithParameter(nameof(proxy), proxy)
            .WithParameter(nameof(timeout), timeout);

    /// <summary>
    /// 视频
    /// </summary>
    /// <param name="file">视频文件名、绝对路径或 URL</param>
    /// <param name="url">视频 URL</param>
    /// <param name="cache">只在通过网络 URL 发送时有效，是否使用已缓存的文件</param>
    /// <param name="proxy">只在通过网络 URL 发送时有效，是否通过代理下载文件</param>
    /// <param name="timeout">只在通过网络 URL 发送时有效，下载超时时间（秒）</param>
    /// <returns></returns>
    public static T Video(string? file = null, string? url = null, bool? cache = null, bool? proxy = null, int? timeout = null) =>
        new T { Type = "video" }
            .WithParameter(nameof(file), file)
            .WithParameter(nameof(url), url)
            .WithParameter(nameof(cache), cache)
            .WithParameter(nameof(proxy), proxy)
            .WithParameter(nameof(timeout), timeout);

    /// <summary>
    /// At
    /// </summary>
    /// <param name="qq">QQ 号</param>
    /// <returns></returns>
    public static T At(string qq) => new T { Type = "at" }
        .WithParameter(nameof(qq), qq);

    /// <summary>
    /// At 全体成员
    /// </summary>
    /// <returns></returns>
    public static T AtAll() => new() { Type = "at", Parameters = { ["qq"] = "all" } };

    /// <summary>
    /// 魔法表情 - 猜拳
    /// </summary>
    /// <returns></returns>
    public static T Rps() => new() { Type = "rps" };

    /// <summary>
    /// 魔法表情 - 骰子
    /// </summary>
    /// <returns></returns>
    public static T Dice() => new() { Type = "dice" };

    /// <summary>
    /// 窗口抖动 仅支持好友消息使用
    /// </summary>
    /// <returns></returns>
    public static T Shake() => new() { Type = "shake" };

    /// <summary>
    /// 戳一戳
    /// </summary>
    /// <param name="type">类型</param>
    /// <param name="id">ID</param>
    /// <returns></returns>
    public static T Poke(string type, string id) =>
        new T { Type = "poke" }
            .WithParameter(nameof(type), type)
            .WithParameter(nameof(id), id);

    /// <summary>
    /// 匿名消息
    /// </summary>
    /// <param name="ignore">失败时是否继续发送</param>
    /// <returns></returns>
    public static T Anonymous(bool? ignore = null) =>
        new T { Type = "anonymous" }
            .WithParameter(nameof(ignore), ignore);

    /// <summary>
    /// 分享
    /// </summary>
    /// <param name="url">链接 URL</param>
    /// <param name="title">标题</param>
    /// <param name="content">可选，内容描述</param>
    /// <param name="image">可选，图片 URL</param>
    /// <returns></returns>
    public static T Share(string url, string title, string? content = null, string? image = null) =>
        new T { Type = "share" }
            .WithParameter(nameof(url), url)
            .WithParameter(nameof(title), title)
            .WithParameter(nameof(content), content)
            .WithParameter(nameof(image), image);

    /// <summary>
    /// 推荐好友/推荐群
    /// </summary>
    /// <param name="type">推荐类型，qq 为推荐好友，group 为推荐群</param>
    /// <param name="id">被推荐的 QQ 号或群号</param>
    /// <returns></returns>
    public static T Contact(string type, string id) =>
        new T { Type = "contact" }
            .WithParameter(nameof(type), type)
            .WithParameter(nameof(id), id);

    /// <summary>
    /// 位置
    /// </summary>
    /// <param name="lat">纬度</param>
    /// <param name="lon">经度</param>
    /// <param name="title">可选，标题</param>
    /// <param name="content">可选，内容描述</param>
    /// <returns></returns>
    public static T Location(double lat, double lon, string? title = null, string? content = null) =>
        new T { Type = "location" }
            .WithParameter(nameof(lat), lat)
            .WithParameter(nameof(lon), lon)
            .WithParameter(nameof(title), title)
            .WithParameter(nameof(content), content);

    /// <summary>
    /// 音乐分享
    /// </summary>
    /// <param name="type">音乐类型，qq、163、xm、custom</param>
    /// <param name="id">歌曲 ID（仅在 type 不为 custom 时使用）</param>
    /// <param name="url">点击后跳转目标 URL（仅在 type 为 custom 时使用）</param>
    /// <param name="audio">音乐 URL（仅在 type 为 custom 时使用）</param>
    /// <param name="title">标题（仅在 type 为 custom 时使用）</param>
    /// <param name="content">可选，内容描述（仅在 type 为 custom 时使用）</param>
    /// <param name="image">可选，图片 URL（仅在 type 为 custom 时使用）</param>
    /// <returns></returns>
    public static T Music(string type, string? id, string? url = null, string? audio = null, string? title = null, string? content = null, string? image = null)
        => new T { Type = "music" }
           .WithParameter(nameof(type), type)
           .WithParameter(nameof(id), id)
           .WithParameter(nameof(url), url)
           .WithParameter(nameof(audio), audio)
           .WithParameter(nameof(title), title)
           .WithParameter(nameof(content), content)
           .WithParameter(nameof(image), image);

    /// <summary>
    /// 回复
    /// 注意: 回复消息段必须在消息的开头，一条消息只能有一个回复
    /// </summary>
    /// <param name="id">消息 ID</param>
    /// <returns></returns>
    public static T Reply(string id) => new T { Type = "reply" }
        .WithParameter(nameof(id), id);

    /// <summary>
    /// 合并转发
    /// </summary>
    /// <param name="id">合并转发 ID，需通过发送合并转发消息获取</param>
    /// <returns></returns>
    public static T Forward(string id) => new T { Type = "forward" }
        .WithParameter(nameof(id), id);

    /// <summary>
    /// 合并转发节点（引用消息）
    /// 注意: node 消息段只能在 send_forward_msg API 中使用
    /// </summary>
    /// <param name="id">转发的消息 ID</param>
    /// <returns></returns>
    public static T NodeReference(string id) => new T { Type = "node" }
        .WithParameter(nameof(id), id);

    /// <summary>
    /// 合并转发节点（自定义消息）
    /// 注意: node 消息段只能在 send_forward_msg API 中使用
    /// </summary>
    /// <param name="user_id">发送者 QQ 号</param>
    /// <param name="nickname">发送者昵称</param>
    /// <param name="content">消息内容</param>
    /// <returns></returns>
    // ReSharper disable once InconsistentNaming
    public static T NodeCustom(string user_id, string nickname, List<T> content) =>
        new T { Type = "node" }
            .WithParameter(nameof(user_id), user_id)
            .WithParameter(nameof(nickname), nickname)
            .WithParameter(nameof(content), content);

    /// <summary>
    /// XML 消息
    /// </summary>
    /// <param name="data">XML 内容</param>
    /// <returns></returns>
    public static T Xml(string data) => new T { Type = "xml" }
        .WithParameter(nameof(data), data);

    /// <summary>
    /// JSON 消息
    /// </summary>
    /// <param name="data">JSON 内容</param>
    /// <returns></returns>
    public static T Json(string data) => new T { Type = "json" }
        .WithParameter(nameof(data), data);
}