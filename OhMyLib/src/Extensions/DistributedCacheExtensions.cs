using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace OhMyLib.Extensions;

public static class DistributedCacheExtensions
{
    extension(IDistributedCache cache)
    {
        public T? GetObject<T>(string key)
        {
            var data = cache.Get(key);
            if (data == null)
                return default;

            var json = Encoding.UTF8.GetString(data);
            return JsonSerializer.Deserialize<T>(json);
        }

        public void SetObject<T>(string key, T value, DistributedCacheEntryOptions? options = null)
        {
            var json = JsonSerializer.Serialize(value);
            var data = Encoding.UTF8.GetBytes(json);
            cache.Set(key, data, options ?? new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2)
            });
        }

        public T GetOrSetObject<T>(string key, Func<T> factory, DistributedCacheEntryOptions? options = null)
        {
            var obj = cache.GetObject<T>(key);
            if (obj != null)
                return obj;

            obj = factory();
            cache.SetObject(key, obj, options);
            return obj;
        }

        public async Task<T?> GetObjectAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            var data = await cache.GetAsync(key, cancellationToken);
            if (data == null)
                return default;

            var json = Encoding.UTF8.GetString(data);
            return JsonSerializer.Deserialize<T>(json);
        }

        public async Task SetObjectAsync<T>(string key, T value, DistributedCacheEntryOptions? options = null, CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(value);
            var data = Encoding.UTF8.GetBytes(json);
            await cache.SetAsync(key, data, options ?? new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2)
            }, cancellationToken);
        }

        public async Task<T> GetOrSetObjectAsync<T>(string key, Func<Task<T>> factory, DistributedCacheEntryOptions? options = null,
                                                    CancellationToken token = default)
        {
            var obj = await cache.GetObjectAsync<T>(key, cancellationToken: token);
            if (obj != null)
                return obj;

            obj = await factory();
            await cache.SetObjectAsync(key, obj, options, cancellationToken: token);
            return obj;
        }
    }
}