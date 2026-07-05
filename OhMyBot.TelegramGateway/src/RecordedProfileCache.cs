using System.Collections.Concurrent;

namespace OhMyBot.TelegramGateway;

/// <summary>
/// 网关侧「最近已记录档案」去重表，避免活跃群里每条消息都对 Core 打一次 RecordUserProfile gRPC。
/// 键为平台 uid，值为档案字段签名 + 过期时刻：同一 uid 且签名未变且未过期则跳过（<see cref="ShouldRecord"/> 返回 false）。
/// 签名含用户名/昵称等字段，任一变化立即失效并重新记录；TTL 到期后即便未变也放行一次（顺带刷新，
/// 兜底 Core 侧记录被清）。仅在 gRPC 成功后由 <see cref="MarkRecorded"/> 落表，失败则下条消息自然重试。
/// </summary>
public sealed class RecordedProfileCache(TimeProvider timeProvider, TimeSpan ttl)
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private long _nextSweepTicks;

    public bool ShouldRecord(string uid, string signature)
    {
        var now = timeProvider.GetUtcNow();
        MaybeSweep(now);
        return !(_entries.TryGetValue(uid, out var entry)
            && entry.ExpiresAt > now
            && string.Equals(entry.Signature, signature, StringComparison.Ordinal));
    }

    public void MarkRecorded(string uid, string signature)
    {
        _entries[uid] = new Entry(signature, timeProvider.GetUtcNow() + ttl);
    }

    // 每 ttl 至多清理一次过期项，防止长期运行下积累已离群/长期沉默用户的条目。
    private void MaybeSweep(DateTimeOffset now)
    {
        if (now.UtcTicks < Interlocked.Read(ref _nextSweepTicks))
        {
            return;
        }

        Interlocked.Exchange(ref _nextSweepTicks, (now + ttl).UtcTicks);
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    private readonly record struct Entry(string Signature, DateTimeOffset ExpiresAt);
}
