namespace OhMyLib.Requests.Kuro.Data;

/**
OhMyBot.Tests.TestKuroApi.TestInitGameSignin

{
  "disposableGoodsList": [],
  "eventEndTimes": "2026-01-31 23:59:59",
  "eventStartTimes": "2026-01-01 00:00:00",
  "expendGold": 200,
  "expendNum": 3,
  "isSigIn": false,
  "nowServerTimes": "2026-01-31 16:15:33",
  "omissionNnm": 22,
  "openNotifica": false,
  "redirectContent": "taskCenter",
  "redirectText": "\u4EFB\u52A1\u4E2D\u5FC3",
  "redirectType": 2,
  "repleNum": 1,
  "sigInNum": 8,
  "signInGoodsConfigs": [
    {
      "goodsId": 43010002,
      "goodsName": "\u4E2D\u7EA7\u5171\u9E23\u4FC3\u5242",
      "goodsNum": 2,
      "goodsUrl": "https://prod-alicdn-community.kurobbs.com/signInIcon/d886f816de6e4cb7be87c8868bcce53520251203.png",
      "id": 2059,
      "isGain": false,
      "serialNum": 0,
      "signId": 60
    },
    {
      "goodsId": 36000002,
      "goodsName": "\u4E2D\u7EA7\u5BC6\u97F3\u7B52",
      "goodsNum": 2,
      "goodsUrl": "https://prod-alicdn-community.kurobbs.com/signInIcon/1c37b7d6fa6a415f9f427f623ce8104d20251203.png",
      "id": 2089,
      "isGain": false,
      "serialNum": 30,
      "signId": 60
    }
  ],
  "signLoopGoodsList": []
}
 */
public class KuroSignInInitData
{
    public List<KuroSignInGoods> DisposableGoodsList { get; set; } = [];
    public string EventStartTimes { get; set; } = string.Empty;
    public string EventEndTimes { get; set; } = string.Empty;
    public int ExpendGold { get; set; }
    public int ExpendNum { get; set; }
    public bool IsSigIn { get; set; }
    public string NowServerTimes { get; set; } = string.Empty;
    public int OmissionNnm { get; set; }
    public bool OpenNotifica { get; set; }
    public string RedirectContent { get; set; } = string.Empty;
    public string RedirectText { get; set; } = string.Empty;
    public int RedirectType { get; set; }
    public int SigInNum { get; set; }
    public int RepleNum { get; set; }
    public List<KuroSignInGoods> SignInGoodsConfigs { get; set; } = [];
    public List<KuroSignInGoods> SignLoopGoodsList { get; set; } = [];

    public class KuroSignInGoods
    {
        public long GoodsId { get; set; }
        public string GoodsName { get; set; } = string.Empty;
        public int GoodsNum { get; set; }
        public string GoodsUrl { get; set; } = string.Empty;
        public long Id { get; set; }
        public bool IsGain { get; set; }
        public int SerialNum { get; set; }
        public long SignId { get; set; }
    }
}