namespace OhMyLib.Requests.Kuro.Data;

/**
         "defaultRoleList": [
            {
                "achievementCount": 462,
                "actionRecoverSwitch": false,
                "activeDay": 163,
                "fashionCollectionPercent": 0,
                "gameHeadUrl": "https://prod-alicdn-community.kurobbs.com/game/mingchaoIcon.png",
                "gameId": 3,
                "gameLevel": "77",
                "headPhotoUrl": "https://web-static.kurobbs.com/adminConfig/139/profile_picture/1752061625952.png",
                "id": "10374827",
                "isDefault": true,
                "phantomPercent": 0.974,
                "roleId": "104186333",
                "roleName": "喵喵",
                "roleNum": 20,
                "serverId": "76402e5b20be2c39f095a152090afddc",
                "serverName": "鸣潮",
                "userId": "30683278",
                "widgetHasPull": false
            }
        ],
        "hasDefaultRole": false,
        "hideRole": false
    },
 */
public class KuroBbsDefaultRoleData
{
    public List<KuroBbsDefaultRoleItem> DefaultRoleList { get; set; } = [];
    public bool HasDefaultRole { get; set; }
    public bool HideRole { get; set; }

    public class KuroBbsDefaultRoleItem
    {
        public int AchievementCount { get; set; }
        public bool ActionRecoverSwitch { get; set; }
        public int ActiveDay { get; set; }
        public double FashionCollectionPercent { get; set; }
        public string GameHeadUrl { get; set; } = string.Empty;
        public int GameId { get; set; }
        public string GameLevel { get; set; } = string.Empty;
        public string HeadPhotoUrl { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public double PhantomPercent { get; set; }
        public string RoleId { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public int RoleNum { get; set; }
        public string ServerId { get; set; } = string.Empty;
        public string ServerName { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public bool WidgetHasPull { get; set; }
    }
}