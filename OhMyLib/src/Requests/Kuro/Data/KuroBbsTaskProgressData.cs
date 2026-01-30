namespace OhMyLib.Requests.Kuro.Data;

/**
    {
        "currentDailyGold": 80,
        "growTask": [
            {
                "completeTimes": 1,
                "gainGold": 120,
                "needActionTimes": 1,
                "process": 1.00,
                "remark": "修改头像",
                "skipType": 1,
                "times": 1
            },
            {
                "completeTimes": 1,
                "gainGold": 60,
                "needActionTimes": 1,
                "process": 1.00,
                "remark": "修改个性签名",
                "skipType": 2,
                "times": 1
            },
            {
                "completeTimes": 1,
                "gainGold": 240,
                "needActionTimes": 1,
                "process": 1.00,
                "remark": "关注3名用户",
                "skipType": 3,
                "times": 1
            },
            {
                "completeTimes": 1,
                "gainGold": 200,
                "needActionTimes": 1,
                "process": 1.00,
                "remark": "累积社区浏览3分钟",
                "skipType": 4,
                "times": 1
            },
            {
                "completeTimes": 1,
                "gainGold": 80,
                "needActionTimes": 1,
                "process": 1.00,
                "remark": "首次兑换奖励",
                "skipType": 5,
                "times": 1
            }
        ],
        "dailyTask": [
            {
                "completeTimes": 1,
                "gainGold": 30,
                "needActionTimes": 1,
                "process": 1.00,
                "remark": "用户签到",
                "skipType": 6,
                "times": 1
            },
            {
                "completeTimes": 3,
                "gainGold": 20,
                "needActionTimes": 3,
                "process": 1.00,
                "remark": "浏览3篇帖子",
                "skipType": 0,
                "times": 1
            },
            {
                "completeTimes": 5,
                "gainGold": 20,
                "needActionTimes": 5,
                "process": 1.00,
                "remark": "点赞5次",
                "skipType": 0,
                "times": 1
            },
            {
                "completeTimes": 1,
                "gainGold": 10,
                "needActionTimes": 1,
                "process": 1.00,
                "remark": "分享1次帖子",
                "skipType": 0,
                "times": 1
            }
        ],
        "maxDailyGold": 80
    }
 */
public class KuroBbsTaskProgressData
{
    public int CurrentDailyGold { get; set; }

    public List<KuroBbsTaskItemData> GrowTask { get; set; } = [];

    public List<KuroBbsTaskItemData> DailyTask { get; set; } = [];

    public int MaxDailyGold { get; set; }

    public class KuroBbsTaskItemData
    {
        public int CompleteTimes { get; set; }

        public int GainGold { get; set; }

        public int NeedActionTimes { get; set; }

        public double Process { get; set; }

        public string Remark { get; set; } = string.Empty;

        public int SkipType { get; set; }

        public int Times { get; set; }

        public bool Finished => CompleteTimes >= NeedActionTimes;
    }
}