namespace OhMyLib.Requests.Kuro.Data;

/**
[
  {
    "goodsId": "2",
    "goodsName": "\u8D1D\u5E01",
    "goodsNum": 8000,
    "goodsUrl": "https://web-static.kurobbs.com/adminConfig/68/goods_icon/1724125590734.png",
    "orderCode": "1467193629965086720",
    "sendState": true,
    "sendStateV2": 1,
    "sigInDate": "2026-01-31 16:23:41",
    "type": 0
  },
  {
    "goodsId": "43020002",
    "goodsName": "\u4E2D\u7EA7\u80FD\u6E90\u6838\u5FC3",
    "goodsNum": 2,
    "goodsUrl": "https://web-static.kurobbs.com/adminConfig/68/goods_icon/1724125579631.png",
    "orderCode": "1466744005731954688",
    "sendState": true,
    "sendStateV2": 1,
    "sigInDate": "2026-01-30 10:37:02",
    "type": 0
  },
  {
    "goodsId": "3",
    "goodsName": "\u661F\u58F0",
    "goodsNum": 20,
    "goodsUrl": "https://web-static.kurobbs.com/adminConfig/68/goods_icon/1724125228687.png",
    "orderCode": "1464574025821376512",
    "sendState": true,
    "sendStateV2": 1,
    "sigInDate": "2026-01-24 10:54:18",
    "type": 0
  },
  {
    "goodsId": "43010002",
    "goodsName": "\u4E2D\u7EA7\u5171\u9E23\u4FC3\u5242",
    "goodsNum": 2,
    "goodsUrl": "https://web-static.kurobbs.com/adminConfig/68/goods_icon/1724125498177.png",
    "orderCode": "1459231523513028608",
    "sendState": true,
    "sendStateV2": 1,
    "sigInDate": "2026-01-09 17:05:06",
    "type": 0
  },
  {
    "goodsId": "3",
    "goodsName": "\u661F\u58F0",
    "goodsNum": 30,
    "goodsUrl": "https://web-static.kurobbs.com/adminConfig/68/goods_icon/1724125228687.png",
    "orderCode": "1459231523529805824",
    "sendState": true,
    "sendStateV2": 1,
    "sigInDate": "2026-01-09 17:05:06",
    "type": 2
  },
  {
    "goodsId": "36000002",
    "goodsName": "\u4E2D\u7EA7\u5BC6\u97F3\u7B52",
    "goodsNum": 1,
    "goodsUrl": "https://web-static.kurobbs.com/adminConfig/68/goods_icon/1724125571048.png",
    "orderCode": "1458739364751355904",
    "sendState": true,
    "sendStateV2": 1,
    "sigInDate": "2026-01-08 08:29:27",
    "type": 0
  },
  {
    "goodsId": "43020003",
    "goodsName": "\u9AD8\u7EA7\u80FD\u6E90\u6838\u5FC3",
    "goodsNum": 3,
    "goodsUrl": "https://web-static.kurobbs.com/adminConfig/68/goods_icon/1724125890562.png",
    "orderCode": "1458739364818464768",
    "sendState": true,
    "sendStateV2": 1,
    "sigInDate": "2026-01-08 08:29:27",
    "type": 3
  }
]
 */
public class KuroGameSignInQueryData
{
    public string GoodsId { get; set; } = string.Empty;
    public string GoodsName { get; set; } = string.Empty;
    public int GoodsNum { get; set; }
    public string GoodsUrl { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;
    public bool SendState { get; set; }
    public int SendStateV2 { get; set; }
    public string SigInDate { get; set; } = string.Empty;
    public int Type { get; set; }
}