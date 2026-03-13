using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace OhMyLib.Extensions;

public static class DistributedCacheExtensions
{
    private const int DefaultCacheDurationHours = 12;

    extension(IDistributedCache cache)
    {
        public T? GetObject<T>(string key, T? defaultValue = default)
        {
            var data = cache.Get(key);
            if (data == null)
                return defaultValue;

            var json = Encoding.UTF8.GetString(data);
            return JsonSerializer.Deserialize<T>(json) ?? defaultValue;
        }

        public void SetObject<T>(string key, T value, DistributedCacheEntryOptions? options = null)
        {
            var json = JsonSerializer.Serialize(value);
            var data = Encoding.UTF8.GetBytes(json);
            cache.Set(key, data, options ?? new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(DefaultCacheDurationHours)
            });
        }

        public T GetOrSetObject<T>(string key, Func<T> factory, DistributedCacheEntryOptions? options = null)
        {
            var data = cache.Get(key);
            if (data != null)
            {
                var json = Encoding.UTF8.GetString(data);
                var o = JsonSerializer.Deserialize<T>(json);
                if (o != null)
                    return o;
            }

            var obj = factory();
            cache.SetObject(key, obj, options);
            return obj;
        }

        public async Task<T?> GetObjectAsync<T>(string key, T? defaultValue = default, CancellationToken cancellationToken = default)
        {
            var data = await cache.GetAsync(key, cancellationToken);
            if (data == null)
                return defaultValue;

            var json = Encoding.UTF8.GetString(data);
            return JsonSerializer.Deserialize<T>(json) ?? defaultValue;
        }

        public async Task SetObjectAsync<T>(string key, T value, DistributedCacheEntryOptions? options = null, CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(value);
            var data = Encoding.UTF8.GetBytes(json);
            await cache.SetAsync(key, data, options ?? new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(DefaultCacheDurationHours)
            }, cancellationToken);
        }

        public async Task<T> GetOrSetObjectAsync<T>(string key, Func<Task<T>> factory, DistributedCacheEntryOptions? options = null,
                                                    CancellationToken token = default)
        {
            var data = await cache.GetAsync(key, token);
            if (data != null)
            {
                var json = Encoding.UTF8.GetString(data);
                var o = JsonSerializer.Deserialize<T>(json);
                if (o != null)
                    return o;
            }

            var obj = await factory();
            await cache.SetObjectAsync(key, obj, options, cancellationToken: token);
            return obj;
        }
    }
}