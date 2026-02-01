using OhMyLib.Attributes.Kuro;

namespace OhMyLib.Enums.Kuro;

[Flags]
public enum KuroGameTaskType : uint
{
    [KuroBbsTask(Name = "无")] None,
    [KuroBbsTask(Name = "签到")] Signin = 1 << 0,
    [KuroBbsTask(Name = "补签")] SigninMakeUp = 1 << 1,
}