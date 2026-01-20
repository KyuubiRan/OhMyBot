namespace OhMyLib.Enums.Kuro;

[Flags]
public enum KuroGameTaskType : uint
{
    None,

    Signin = 1 << 0,       // 签到
    SigninMakeUp = 1 << 1, // 补签
}