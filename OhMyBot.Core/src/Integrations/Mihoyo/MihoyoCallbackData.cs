namespace OhMyBot.Core.Integrations.Mihoyo;

public sealed record MihoyoAccountCallbackData(long AccountId);

public sealed record MihoyoBbsSignCallbackData(long AccountId, string[] Actions);

public sealed record MihoyoBbsSignAllCallbackData;

public sealed record MihoyoGameSignCallbackData(long AccountId, string[] GameKeys);

/// <summary>
/// 游戏签到勾选面板。Toggle 非空时表示本次点击要翻转的游戏 Key；为空表示仅打开/刷新面板。
/// 勾选状态由服务端（账号记录）持久化，不再随按钮快照传递，避免并发点击互相覆盖。
/// </summary>
public sealed record MihoyoGameSignPanelCallbackData(long AccountId, string? Toggle = null);

public sealed record MihoyoGameSignAllCallbackData;

public sealed record MihoyoGameSignBackCallbackData;

public sealed record MihoyoAutoSignCallbackData(long AccountId);

public sealed record MihoyoBbsTaskCallbackData(long AccountId, long TaskFlag);

public sealed record MihoyoBbsTaskToggleAllCallbackData(long AccountId);

public sealed record MihoyoGameAutoSignCallbackData(long RoleId, long AccountId = 0, int Page = 0);

public sealed record MihoyoGameAutoSignToggleAllCallbackData(long AccountId, int Page = 0);

public sealed record MihoyoAutoSignMenuCallbackData(long AccountId, string Level, int Page = 0);

public sealed record MihoyoDeleteConfirmCallbackData(long AccountId, bool Confirm);
