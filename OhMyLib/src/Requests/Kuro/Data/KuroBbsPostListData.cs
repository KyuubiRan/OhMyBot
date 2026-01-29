namespace OhMyLib.Requests.Kuro.Data;

/**
   {
       "browseCount": "18020",
       "commentCount": 77,
       "coverImages": [
           {
               "imgHeight": 810,
               "imgWidth": 1440,
               "index": 0,
               "pointOffsetX": 0,
               "pointOffsetY": 403,
               "sourceUrl": "https://prod-alicdn-community.kurobbs.com/forum/6a2424d6d26149f987bc68ffb2f8a4f520260128.jpeg",
               "url": "https://prod-alicdn-community.kurobbs.com/forum/d06c2ac1b1c14ba4bb9ab17951da440720260128.jpeg"
           }
       ],
       "createTimestamp": "1769570825000",
       "gameForumId": 10,
       "gameId": 3,
       "gameName": "鸣潮",
       "identifyClassify": 1,
       "identifyNames": "《鸣潮》项目组指定联络站",
       "imgContent": [
           {
               "imgHeight": 3050,
               "imgWidth": 1440,
               "url": "https://prod-alicdn-community.kurobbs.com/forum/6a2424d6d26149f987bc68ffb2f8a4f520260128.jpeg"
           }
       ],
       "imgCount": 1,
       "ipRegion": "地点未知",
       "isFollow": 0,
       "isLike": 0,
       "isLock": 0,
       "isPublisher": 0,
       "lastEditIpRegion": "地点未知",
       "likeCount": 3206,
       "newIdentifyNames": [
           "《鸣潮》项目组指定联络站"
       ],
       "postContent": "「2026鸣潮新春会：烟花绽放时」直播将于1月30日19：00正式开启！",
       "postId": "1466031829368872960",
       "postTitle": "✦ 2026鸣潮新春会：倒计时2天 ✦",
       "postType": 1,
       "reviewStatus": 1,
       "showTime": "17小时前",
       "topicList": [
        {
            "postId": "1464965877514870784",
            "topicId": 336,
            "topicName": "爱弥斯"
        },
        {
            "postId": "1464965877514870784",
            "topicId": 341,
            "topicName": "赠予雪中的你"
        },
        {
            "postId": "1464965877514870784",
            "topicId": 331,
            "topicName": "3.0版本攻略"
        },
        {
            "postId": "1464965877514870784",
            "topicId": 332,
            "topicName": "3.0版本万象"
        }],
       "userHeadUrl": "https://prod-alicdn-community.kurobbs.com/headCode/akishu.png",
       "userId": "10381395",
       "userLevel": 0,
       "userName": "哒哒情报站"
   }
 */
public class KuroBbsPostListData
{
    public string BrowseCount { get; set; } = string.Empty;

    public int CommentCount { get; set; }

    public List<KuroBbsImageData> CoverImages { get; set; } = [];

    public string CreateTimestamp { get; set; } = string.Empty;

    public long GameForumId { get; set; }

    public long GameId { get; set; }

    public string GameName { get; set; } = string.Empty;

    public int IdentifyClassify { get; set; }

    public string IdentifyNames { get; set; } = string.Empty;

    public List<KuroBbsImageData> ImgContent { get; set; } = [];

    public int ImgCount { get; set; }

    public string IpRegion { get; set; } = string.Empty;

    public int IsFollow { get; set; }

    public int IsLike { get; set; }

    public int IsLock { get; set; }

    public int IsPublisher { get; set; }

    public string LastEditIpRegion { get; set; } = string.Empty;

    public int LikeCount { get; set; }

    public List<string> NewIdentifyNames { get; set; } = [];

    public string PostContent { get; set; } = string.Empty;

    public string PostId { get; set; } = string.Empty;

    public string PostTitle { get; set; } = string.Empty;

    public int PostType { get; set; }

    public int ReviewStatus { get; set; }

    public string ShowTime { get; set; } = string.Empty;

    public List<KuroBbsTopicData> TopicList { get; set; } = [];

    public string UserHeadUrl { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public int UserLevel { get; set; }

    public string UserName { get; set; } = string.Empty;
}