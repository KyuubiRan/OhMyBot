namespace OhMyLib.Extensions;

public static class DictExtensions
{
    extension<TKey, TValue>(Dictionary<TKey, TValue> dict) where TKey : notnull
    {
        public Dictionary<TKey, TValue> Merge(Dictionary<TKey, TValue> other)
        {
            var result = new Dictionary<TKey, TValue>(dict);
            foreach (var kv in other)
            {
                result[kv.Key] = kv.Value;
            }
            return result;
        }
    }
}