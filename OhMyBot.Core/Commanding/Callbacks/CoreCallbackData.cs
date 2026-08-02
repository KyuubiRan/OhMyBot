using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Commanding.Callbacks;

public sealed record SetPrivilegeCallbackData(
    BotPlatform Platform,
    string Uid,
    UserPrivilege Privilege);

public sealed record NotifyTypeCallbackData(string Type);

public sealed record NotifyAccountCallbackData(string Type, long AccountId, bool ToggleAll = false);

public sealed record NotifyBackCallbackData;
