namespace OhMyBot.Core.Kuro;

public sealed record KuroAccountCallbackData(long AccountId);

public sealed record KuroBbsSignCallbackData(long AccountId, string[] Actions);

public sealed record KuroGameSignCallbackData(long AccountId, long[] GameIds);

public sealed record KuroAutoSignCallbackData(long AccountId);

public sealed record KuroBbsTaskCallbackData(long AccountId, long TaskFlag);

public sealed record KuroBbsTaskToggleAllCallbackData(long AccountId);

public sealed record KuroGameAutoSignCallbackData(long RoleId, long AccountId = 0, int Page = 0);

public sealed record KuroGameAutoSignToggleAllCallbackData(long AccountId, int Page = 0);

public sealed record KuroAutoSignMenuCallbackData(long AccountId, string Level, int Page = 0);

public sealed record KuroDeleteConfirmCallbackData(long AccountId, bool Confirm);
