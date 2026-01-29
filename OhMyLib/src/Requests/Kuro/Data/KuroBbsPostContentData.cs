namespace OhMyLib.Requests.Kuro.Data;

/**
{
    "contentType": 2,
    "imgHeight": 1050,
    "imgWidth": 970,
    "isCover": 0,
    "topicFlag": 0,
    "url": "https://prod-alicdn-community.kurobbs.com/forum/48933cc38aeb498c81711a21fea8b2c920250320.png"
}
 */
public class KuroBbsPostContentData
{
    public int? ContentType { get; set; } = null;
    public int? ImgHeight { get; set; } = null;
    public int? ImgWidth { get; set; } = null;
    public int? IsCover { get; set; } = null;
    public int? TopicFlag { get; set; } = null;
    public string? Url { get; set; } = null;
}