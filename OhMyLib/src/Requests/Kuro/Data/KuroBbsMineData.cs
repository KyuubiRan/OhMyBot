namespace OhMyLib.Requests.Kuro.Data;

/**
{
    "collectCount": 0,
    "commentCount": 0,
    "fansCount": 0,
    "fansNewCount": 0,
    "followCount": 1,
    "gender": 3,
    "goldNum": 90,
    "headId": 168,
    "headUrl": "https://prod-alicdn-community.kurobbs.com/newHead/aki/katixiya.png",
    "hiddenFans": 0,
    "ipRegion": "未知属地",
    "isFollow": 0,
    "isLoginUser": 1,
    "isMute": 0,
    "lastLoginModelType": "PKX110",
    "lastLoginTime": "2026-01-29 11:06:57",
    "likeCount": 0,
    "mobile": "1********9",
    "postCount": 0,
    "registerTime": "2025.12.12",
    "signature": "这个人很懒，什么都没有留下",
    "signatureReview": "这个人很懒，什么都没有留下",
    "signatureReviewStatus": 1,
    "status": 0,
    "userId": "123456",
    "userName": "用户名"
}
 */
public class KuroBbsMineData
{
    public MineData? Mine { get; set; } = null;
    
    public class MineData
    {
        public int? CollectCount { get; set; } = null;
        public int? CommentCount { get; set; } = null;
        public int? FansCount { get; set; } = null;
        public int? FansNewCount { get; set; } = null;
        public int? FollowCount { get; set; } = null;
        public int? Gender { get; set; } = null;
        public int? GoldNum { get; set; } = null;
        public int? HeadId { get; set; } = null;
        public string? HeadUrl { get; set; } = null;
        public int? HiddenFans { get; set; } = null;
        public string? IpRegion { get; set; } = null;
        public int? IsFollow { get; set; } = null;
        public int? IsLoginUser { get; set; } = null;
        public int? IsMute { get; set; } = null;
        public string? LastLoginModelType { get; set; } = null;
        public string? LastLoginTime { get; set; } = null;
        public int? LikeCount { get; set; } = null;
        public string? Mobile { get; set; } = null;
        public int? PostCount { get; set; } = null;
        public string? RegisterTime { get; set; } = null;
        public string? Signature { get; set; } = null;
        public string? SignatureReview { get; set; } = null;
        public int? SignatureReviewStatus { get; set; } = null;
        public int? Status { get; set; } = null;
        public string? UserId { get; set; } = null;
        public string? UserName { get; set; } = null;
    }
}