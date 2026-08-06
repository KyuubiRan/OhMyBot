namespace OhMyBot.Core.Commanding.Commands;

/// <summary>
/// 面向用户的业务失败：消息本身就是给用户看的自助提示（密码错、Token 失效、账号已被他人绑定）。
///
/// 与普通异常的区别只在 <see cref="CommandExecutionService"/> 的 catch 顺序：普通异常一律折叠成
/// 「请稍后重试 +错误 id」，用户拿不到任何可操作信息，只能来问；这一类则原样回给用户。
///
/// 抛之前必须确认消息里没有表名/约束名/内网地址/上游响应原文——它会直接进聊天窗口。
/// 拿不准的（比如把上游 raw body 拼进去的）就继续抛普通异常，走兜底。
/// </summary>
public sealed class CommandUserException(string errorCode, string message) : Exception(message)
{
    /// <summary>回给平台的错误码，取值与 <see cref="CommandResponses.Error"/> 的 errorCode 同一套。</summary>
    public string ErrorCode { get; } = errorCode;
}
