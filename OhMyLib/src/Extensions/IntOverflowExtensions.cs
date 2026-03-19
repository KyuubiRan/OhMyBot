namespace OhMyLib.Extensions;

public static class IntOverflowExtensions
{
    public static int ClampToInt(this long value)
    {
        if (value > int.MaxValue) return int.MaxValue;
        if (value < int.MinValue) return int.MinValue;
        return (int)value;
    }
    
    extension(int a)
    {
        public int SaturatingAdd(int b)
        {
            return ((long)a + b).ClampToInt();
        }

        public int SaturatingSubtract(int b)
        {
            return ((long)a - b).ClampToInt();
        }

        public int SaturatingMultiply(int b)
        {
            return ((long)a * b).ClampToInt();
        }
    }

    public static int SaturatingCompute(int a, int b, Func<long, long, long> operation)
    {
        return operation(a, b).ClampToInt();
    }
}