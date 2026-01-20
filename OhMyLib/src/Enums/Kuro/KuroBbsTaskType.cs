namespace OhMyLib.Enums.Kuro;

[Flags]
public enum KuroBbsTaskType : uint
{
    None = 0,

    Signin = 1 << 0,
    ViewPosts = 1 << 1,
    LikePosts = 1 << 2,
    SharePosts = 1 << 3,

    All = Signin | ViewPosts | LikePosts | SharePosts,
}