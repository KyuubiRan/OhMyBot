namespace OhMyLib.Requests.Kuro.Data;

/**
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
        }
 */
public class KuroBbsGameForumVo
{
    public string? FilterOfficalUserIds { get; set; } = null;
    public int? ForumDataType { get; set; } = null;
    public int? ForumListShowType { get; set; } = null;
    public int? ForumType { get; set; } = null;
    public int? ForumUiType { get; set; } = null;
    public int? GameId { get; set; } = null;
    public int? Id { get; set; } = null;
    public int? IsOfficial { get; set; } = null;
    public int? IsSpecial { get; set; } = null;
    public string? Name { get; set; } = null;
    public int? RangeDay { get; set; } = null;
    public int? Sort { get; set; } = null;
}