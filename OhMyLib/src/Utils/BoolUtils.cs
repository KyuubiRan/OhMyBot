namespace OhMyLib.Utils;

public static class BoolUtils
{
    public static bool TryParse(string str, out bool? value)
    {
        switch (str.ToLower())
        {
            case "1":
            case "on":
            case "yes":
            case "y":
            case "true":
                value = true;
                return true;

            case "0":
            case "off":
            case "no":
            case "n":
            case "false":
                value = false;¬
                return true;

            default:
                value = null;
                return false;
        }
    }
}