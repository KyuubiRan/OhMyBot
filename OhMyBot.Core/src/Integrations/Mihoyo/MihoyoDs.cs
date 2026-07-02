using System.Security.Cryptography;
using System.Text;

namespace OhMyBot.Core.Integrations.Mihoyo;

/// <summary>
/// 米游社 DS(Dynamic Secret) 签名，移植自 MihoyoBBSTools/tools.py。
/// </summary>
public static class MihoyoDs
{
    private const string RandomTextCharset = "abcdefghijklmnopqrstuvwxyz0123456789";

    private static readonly Guid NamespaceUrl = new("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>get_ds：md5("salt={salt}&t={t}&r={r}")，r 为 6 位随机字符。</summary>
    public static string GetDs(string salt)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var r = RandomText(6);
        return BuildDs(salt, t, r);
    }

    /// <summary>get_ds2：md5("salt={salt}&t={t}&r={r}&b={body}&q={query}")，r 为 100001..200000 随机整数。</summary>
    public static string GetDs2(string salt, string query, string body)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var r = Random.Shared.Next(100001, 200001);
        return BuildDs2(salt, t, r, query, body);
    }

    public static string BuildDs(string salt, long t, string r)
    {
        var c = Md5($"salt={salt}&t={t}&r={r}");
        return $"{t},{r},{c}";
    }

    public static string BuildDs2(string salt, long t, int r, string query, string body)
    {
        var c = Md5($"salt={salt}&t={t}&r={r}&b={body}&q={query}");
        return $"{t},{r},{c}";
    }

    internal static string Md5(string text)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(bytes);
    }

    private static string RandomText(int length)
    {
        return string.Create(length, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = RandomTextCharset[Random.Shared.Next(RandomTextCharset.Length)];
            }
        });
    }

    /// <summary>uuid3(NAMESPACE_URL, cookie)，移植自 tools.get_device_id。</summary>
    public static string GetDeviceId(string cookie)
    {
        return GuidV3(NamespaceUrl, cookie).ToString();
    }

    private static Guid GuidV3(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var combined = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, combined, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, combined, namespaceBytes.Length, nameBytes.Length);

        var hash = MD5.HashData(combined);
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);

        // version 3
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x30);
        // variant RFC 4122
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        SwapByteOrder(guidBytes);
        return new Guid(guidBytes);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}
