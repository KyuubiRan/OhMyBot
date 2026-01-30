namespace OhMyLib.Requests.Kuro.Data;

/**
    "todayList": [
      {
        "goodsId": 43020002,
        "goodsNum": 2,
        "goodsUrl": "https://prod-alicdn-community.kurobbs.com/signInIcon/1b9893cbd9f6456482f3784facef42c720251203.png",
        "type": 0
      }
    ],
    "tomorrowList": [
      {
        "goodsId": 2,
        "goodsNum": 8000,
        "goodsUrl": "https://prod-alicdn-community.kurobbs.com/signInIcon/6e020a7670b54d9ca95b00298d24b8ce20251203.png",
        "type": 0
      }
    ]
 */
public class KuroGameSignInResult
{
    public List<KuroGameSignInItem> TodayList { get; set; } = [];
    public List<KuroGameSignInItem> TomorrowList { get; set; } = [];

    public class KuroGameSignInItem
    {
        public int GoodsId { get; set; }
        public int GoodsNum { get; set; }
        public string GoodsUrl { get; set; } = string.Empty;
        public int Type { get; set; }
    }
}