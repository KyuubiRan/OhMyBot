namespace OhMyBot.Core.Integrations.Kuro;

public sealed record KuroAccountCallbackData(long AccountId);

public sealed record KuroBbsSignCallbackData(long AccountId, string[] Actions);

public sealed record KuroBbsSignAllCallbackData;

public sealed record KuroGameSignCallbackData(long AccountId, long[] GameIds);

/// <summary>
/// 游戏签到勾选面板。Toggle 非 0 时表示本次点击要翻转的游戏 Id；为 0 表示仅打开/刷新面板。
/// 勾选状态由服务端（账号记录）持久化，不再随按钮快照传递，避免并发点击互相覆盖。
/// </summary>
public sealed record KuroGameSignPanelCallbackData(long AccountId, long Toggle = 0);

public sealed record KuroGameSignAllCallbackData;

public sealed record KuroGameSignBackCallbackData;

public sealed record KuroAutoSignCallbackData(long AccountId);

public sealed record KuroBbsTaskCallbackData(long AccountId, long TaskFlag);

public sealed record KuroBbsTaskToggleAllCallbackData(long AccountId);

public sealed record KuroGameAutoSignCallbackData(long RoleId, long AccountId = 0, int Page = 0);

public sealed record KuroGameAutoSignToggleAllCallbackData(long AccountId, int Page = 0);

public sealed record KuroAutoSignMenuCallbackData(long AccountId, string Level, int Page = 0);

public sealed record KuroDeleteConfirmCallbackData(long AccountId, bool Confirm);
