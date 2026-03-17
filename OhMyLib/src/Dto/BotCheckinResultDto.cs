using System.Text;
using FoxTail.Extensions;

namespace OhMyLib.Dto;

public class BotCheckinResultDto
{
    public enum ResultType
    {
        Success,
        Failure,
        UserNotFound,
        AlreadyCheckedIn
    }

    public DateTime Time { get; init; } = DateTime.Now;
    public ResultType Result { get; init; }
    public int TotalDays { get; init; } = 0;
    public int StreakDays { get; init; } = 0;
    public int MaxStreakDays { get; init; } = 0;
    public int CoinGain { get; init; } = 0;
    public int TotalCoins { get; init; } = 0;
    
    public override string ToString()
    {
        if (Result is ResultType.UserNotFound or ResultType.Failure)
            return "签到失败";

        var sb = new StringBuilder();

        sb.AppendLine(Result switch
        {
            ResultType.Success => "签到成功",
            ResultType.AlreadyCheckedIn => "今日已签到",
            _ => ""
        });

        if (DateTime.Now.IsThursday)
        {
            sb.AppendLine("今天是疯狂星期四，额外V你50哈狐币！");
        }
        
        sb.AppendLine($"获得哈狐币: {CoinGain}");
        sb.AppendLine($"当前哈狐币：{TotalCoins}");
        sb.AppendLine($"总签到天数: {TotalDays}");
        sb.AppendLine($"当前连续签到天数: {StreakDays}");
        sb.AppendLine($"最长连续签到天数: {MaxStreakDays}");
        sb.AppendLine($"签到时间: {Time.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

        return sb.ToString().Trim();
    }
}