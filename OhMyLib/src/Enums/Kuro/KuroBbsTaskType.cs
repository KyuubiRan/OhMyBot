using OhMyLib.Attributes.Kuro;

namespace OhMyLib.Enums.Kuro;

[Flags]
public enum KuroBbsTaskType : uint
{
    [KuroBbsTask(Name = "无")] None = 0,

    [KuroBbsTask(Name = "社区签到")] Signin = 1 << 0,
    [KuroBbsTask(Name = "浏览帖子")] ViewPosts = 1 << 1,
    [KuroBbsTask(Name = "点赞帖子")] LikePosts = 1 << 2,
    [KuroBbsTask(Name = "分享帖子")] SharePosts = 1 << 3,
}

public static class KuroBbsTaskTypeConsts
{
    public const KuroBbsTaskType All = KuroBbsTaskType.Signin | KuroBbsTaskType.ViewPosts | KuroBbsTaskType.LikePosts | KuroBbsTaskType.SharePosts;
}