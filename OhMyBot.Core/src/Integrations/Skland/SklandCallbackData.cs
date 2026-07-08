namespace OhMyBot.Core.Integrations.Skland;

public sealed record SklandAccountCallbackData(long AccountId);

public sealed record SklandGameSignCallbackData(long AccountId, long[] RoleIds);

/// <summary>
/// 游戏签到勾选面板。Toggle 非空时表示本次点击要翻转的游戏 appCode；为空表示仅打开/刷新面板。
/// </summary>
public sealed record SklandGameSignPanelCallbackData(long AccountId, string? Toggle = null);

public sealed record SklandGameSignAllCallbackData;

public sealed record SklandGameSignBackCallbackData;

public sealed record SklandAutoSignCallbackData(long AccountId);

public sealed record SklandGameAutoSignCallbackData(long RoleId, long AccountId = 0, int Page = 0);

public sealed record SklandGameAutoSignToggleAllCallbackData(long AccountId, int Page = 0);

public sealed record SklandAutoSignMenuCallbackData(long AccountId, string Level, int Page = 0);

public sealed record SklandDeleteConfirmCallbackData(long AccountId, bool Confirm);
