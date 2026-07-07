namespace OhMyBot.Core.Integrations.Mihoyo;

public sealed class MihoyoOptions
{
    // Salt（跟随米游社版本更新，version 与 salt 相互对应）
    public string SaltApp { get; set; } = "47f15f1b66bee46b816115d8e8e6ebb6";

    public string SaltWeb { get; set; } = "d9200c846b10886e8c874fc33c8f308b";

    public string SaltX6 { get; set; } = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";

    public string VerifyKey { get; set; } = "bll8iq97cem8";

    public string Version { get; set; } = "2.109.0";

    // 1=ios 2=android
    public string ClientType { get; set; } = "2";

    // 4=pc web 5=mobile web
    public string ClientTypeWeb { get; set; } = "5";

    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Linux; Android 12; Unspecified Device) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Version/4.0 Chrome/103.0.5060.129 Mobile Safari/537.36 miHoYoBBS/2.109.0";

    public string OkHttpUserAgent { get; set; } = "okhttp/4.9.3";

    // 主机
    public string WebApi { get; set; } = "https://api-takumi.mihoyo.com";

    /// <summary>HoYoLAB（国际服）账号接口主机，用于绑定时探测账号归属。</summary>
    public string OsWebApi { get; set; } = "https://api-account-os.hoyolab.com";

    public string BbsApi { get; set; } = "https://bbs-api.miyoushe.com";

    public string ZzzWebApi { get; set; } = "https://act-nap-api.mihoyo.com";

    public string CnGameLang { get; set; } = "zh-cn";

    public string OsGameLang { get; set; } = "zh-cn";

    /// <summary>米游币讨论区签到的分区 gids（完成每日签到任务一个分区即可）。</summary>
    public string BbsSignGids { get; set; } = "2";

    /// <summary>看帖/点赞/分享取帖的分区 forumId（默认原神 26）。</summary>
    public string BbsForumId { get; set; } = "26";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public string AccountInfoUrl => WebApi + "/binding/api/getUserGameRolesByCookie";

    /// <summary>国际服(HoYoLAB) 按 Cookie 查询游戏角色，用于绑定时探测账号归属。</summary>
    public string OsAccountInfoUrl => OsWebApi + "/account/binding/api/getUserGameRolesByCookie";

    public string CookieTokenByStokenUrl => WebApi + "/auth/api/getCookieAccountInfoBySToken";

    public string CnEventLunaBase => WebApi + "/event/luna";

    public string ZzzEventLunaBase => ZzzWebApi + "/event/luna/zzz";

    public string BbsTasksListUrl => BbsApi + "/apihub/wapi/getUserMissionsState";

    public string BbsSignUrl => BbsApi + "/apihub/app/api/signIn";

    public string BbsPostListUrl => BbsApi + "/post/api/getForumPostList";

    public string BbsPostDetailUrl => BbsApi + "/post/api/getPostFull";

    public string BbsShareUrl => BbsApi + "/apihub/api/getShareConf";

    public string BbsLikeUrl => BbsApi + "/apihub/sapi/upvotePost";
}
