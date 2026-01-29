namespace OhMyLib.Requests.Kuro.Data;

/**
{
    "activityId": "",
    "gameId": 3,
    "isCollect": 0,
    "isFollow": 0,
    "isLike": 0,
    "postDetail": {
        "appealing": false,
        "browseCount": "90107",
        "collectionCount": 608,
        "commentCount": 184,
        "coverImages": [
            {
                "imgHeight": 1050,
                "imgWidth": 810,
                "index": 0,
                "pointOffsetX": 80,
                "pointOffsetY": 0,
                "sourceUrl": "https://prod-alicdn-community.kurobbs.com/forum/48933cc38aeb498c81711a21fea8b2c920250320.png",
                "url": "https://prod-alicdn-community.kurobbs.com/forum/ead0a71d5269470884ab9f27994f0e9720250320.jpg"
            }
        ],
        "createTimestamp": "1742471096000",
        "gameForumId": 11,
        "gameForumVo": {
            "filterOfficalUserIds": "10012001,10013002,10381395,10381279",
            "forumDataType": 3,
            "forumListShowType": 1,
            "forumType": 1,
            "forumUiType": 3,
            "gameId": 3,
            "id": 11,
            "isOfficial": 0,
            "isSpecial": 0,
            "name": "同人",
            "rangeDay": 7,
            "sort": 6
        },
        "gameId": 3,
        "gameName": "鸣潮",
        "headCodeUrl": "https://prod-alicdn-community.kurobbs.com/newHead/aki/jinxi.png",
        "headFrameUrl": "https://prod-alicdn-community.kurobbs.com/avatar/20250902/48739be0f35544c09eee98b357b8b92820250902.png",
        "id": "1352367327519592448",
        "identifyClassify": 2,
        "identifyNames": "同人画师",
        "ipRegion": "贵州",
        "isCopyright": true,
        "isElite": 0,
        "isLock": 0,
        "isMine": 0,
        "isOfficial": 0,
        "isRecommend": 1,
        "isTop": 0,
        "isTransCode": false,
        "lastEditIpRegion": "贵州",
        "lastEditorTime": "2025-09-12 16:06",
        "likeCount": 68355,
        "newIdentifyNames": [
            "同人画师"
        ],
        "playCount": 1,
        "postContent": [
            {
                "contentType": 2,
                "imgHeight": 1050,
                "imgWidth": 970,
                "isCover": 0,
                "topicFlag": 0,
                "url": "https://prod-alicdn-community.kurobbs.com/forum/48933cc38aeb498c81711a21fea8b2c920250320.png"
            }
        ],
        "postH5Content": "\n <div class=\"w-e_img-card\" data-module-name=\"img-card\" has-delete=\"true\" contenteditable=\"false\"> \n  <img class=\"w_e_network_image\" img-des=\"970,1050,https://prod-alicdn-community.kurobbs.com/forum/48933cc38aeb498c81711a21fea8b2c920250320.png\" listen-img-load=\"true\" src=\"https://prod-alicdn-community.kurobbs.com/forum/48933cc38aeb498c81711a21fea8b2c920250320.png\" style=\"max-width:100%;height:auto\">  \n </div> \n",
        "postNewH5Content": "\n <div class=\"w-e_img-card\" data-module-name=\"img-card\" has-delete=\"true\" contenteditable=\"false\"> \n  <img class=\"w_e_network_image\" img-des=\"970,1050,https://prod-alicdn-community.kurobbs.com/forum/48933cc38aeb498c81711a21fea8b2c920250320.png\" listen-img-load=\"true\" src=\"https://prod-alicdn-community.kurobbs.com/forum/48933cc38aeb498c81711a21fea8b2c920250320.png\" style=\"max-width:100%;height:auto\">  \n </div> \n",
        "postTime": "2025-03-20 19:44",
        "postTitle": "当我用我的画风画汐汐",
        "postType": 1,
        "postUserId": "26210827",
        "publishType": 0,
        "reviewStatus": 1,
        "showRange": 0,
        "topicList": [],
        "userHeadCode": "137",
        "userLevel": 0,
        "userName": "不爱出门版一绘绘"
    }
}
 */
public class KuroBbsPostDetail
{
    public string ActivityId { get; set; } = string.Empty;
    public int GameId { get; set; }
    public int IsCollect { get; set; }
    public int IsFollow { get; set; }
    public int IsLike { get; set; }
    public KuroBbsPostDetailData PostDetail { get; set; } = new KuroBbsPostDetailData();

    public class KuroBbsPostDetailData
    {
        public bool Appealing { get; set; }
        public string BrowseCount { get; set; } = string.Empty;
        public int CollectionCount { get; set; }
        public int CommentCount { get; set; }
        public List<KuroBbsImageData> CoverImages { get; set; } = [];
        public string CreateTimestamp { get; set; } = string.Empty;
        public int GameForumId { get; set; }
        public KuroBbsGameForumVo? GameForumVo { get; set; } = null;
        public int GameId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public string HeadCodeUrl { get; set; } = string.Empty;
        public string HeadFrameUrl { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public int IdentifyClassify { get; set; }
        public string IdentifyNames { get; set; } = string.Empty;
        public string IpRegion { get; set; } = string.Empty;
        public bool IsCopyright { get; set; }
        public int IsElite { get; set; }
        public int IsLock { get; set; }
        public int IsMine { get; set; }
        public int IsOfficial { get; set; }
        public int IsRecommend { get; set; }
        public int IsTop { get; set; }
        public bool IsTransCode { get; set; }
        public string LastEditIpRegion { get; set; } = string.Empty;
        public string LastEditorTime { get; set; } = string.Empty;
        public int LikeCount { get; set; }
        public List<string> NewIdentifyNames { get; set; } = [];
        public int PlayCount { get; set; }
        public List<KuroBbsPostContentData> PostContent { get; set; } = [];
        public string PostH5Content { get; set; } = string.Empty;
        public string PostNewH5Content { get; set; } = string.Empty;
        public string PostTime { get; set; } = string.Empty;
        public string PostTitle { get; set; } = string.Empty;
        public int PostType { get; set; }
        public string PostUserId { get; set; } = string.Empty;
        public int PublishType { get; set; }
        public int ReviewStatus { get; set; }
        public int ShowRange { get; set; }
        public List<KuroBbsTopicData> TopicList { get; set; } = [];
        public string UserHeadCode { get; set; } = string.Empty;
        public int UserLevel { get; set; }
        public string UserName { get; set; } = string.Empty;
    }
}