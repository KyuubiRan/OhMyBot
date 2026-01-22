using OhMyLib.Attributes.Kuro;

namespace OhMyLib.Enums.Kuro;

public enum KuroGameType : byte
{
    [KuroGameAttr(Name = "战双帕弥什", ServerId = "1000")]
    Pgr = 2,

    [KuroGameAttr(Name = "鸣潮", ServerId = "76402e5b20be2c39f095a152090afddc")]
    Wuwa = 3,
}