using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Dto;
using OhMyLib.Enums;
using OhMyLib.Models.Common;

namespace OhMyLib.Services;

[Component]
public class BotUserCheckinService(BotUserService userService)
{
    public async Task<BotCheckinResultDto> CheckinAsync(string id, SoftwareType type, CancellationToken cancellationToken = default)
    {
        var user = await userService.GetUserAsync(id, type, cancellationToken);
        if (user == null)
        {
            return new()
            {
                Result = BotCheckinResultDto.ResultType.UserNotFound
            };
        }

        var now = DateTimeOffset.UtcNow;

        var checkinState = user.UserCheckin ??= new BotUserCheckin();

        if (checkinState.LastCheckinTime?.Date == now.Date)
        {
            return new()
            {
                Result = BotCheckinResultDto.ResultType.AlreadyCheckedIn,
                TotalDays = checkinState.TotalDays,
                TotalCoins = user.Coin,
                StreakDays = checkinState.StreakDays,
                MaxStreakDays = checkinState.MaxStreakDays,
                CoinGain = checkinState.StreakDays * 9 + (DateTime.Now.IsThursday ? +50 : 0),
            };
        }

        var lastCheckinDate = checkinState.LastCheckinTime?.Date;

        var breakStreak = lastCheckinDate != null && Math.Abs((lastCheckinDate.Value - now.Date).TotalDays) > 1;

        checkinState.LastCheckinTime = DateTimeOffset.UtcNow;
        checkinState.TotalDays++;
        checkinState.StreakDays = breakStreak ? 1 : checkinState.StreakDays + 1;
        checkinState.MaxStreakDays = Math.Max(checkinState.MaxStreakDays, checkinState.StreakDays);
        var reward = checkinState.StreakDays * 9 + (DateTime.Now.IsThursday ? +50 : 0);
        user.Coin += reward;

        await userService.SaveAsync(cancellationToken);

        await userService.InvalidateCacheAsync(user.OwnerId, SoftwareType.Telegram);

        return new()
        {
            Result = BotCheckinResultDto.ResultType.Success,
            TotalDays = checkinState.TotalDays,
            TotalCoins = user.Coin,
            StreakDays = checkinState.StreakDays,
            MaxStreakDays = checkinState.MaxStreakDays,
            CoinGain = reward,
        };
    }
}