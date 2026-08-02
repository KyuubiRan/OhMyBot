using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace OhMyBot.Core.Infrastructure.Security;

public sealed class AesGcmSecretProtector(IOptions<EncryptionOptions> options) : ISecretProtector
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key = DecodeKey(options.Value.Key);

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);

        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported encrypted secret format.");
        }

        var payload = Convert.FromBase64String(ciphertext[Prefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("Encrypted secret payload is invalid.");
        }

        var nonce = payload[..NonceSize];
        var tag = payload[NonceSize..(NonceSize + TagSize)];
        var encrypted = payload[(NonceSize + TagSize)..];
        var plaintext = new byte[encrypted.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, encrypted, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DecodeKey(string configuredKey)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            throw new InvalidOperationException("Encryption:Key is required.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configuredKey);
        }
        catch (FormatException)
        {
            // 不把 FormatException 作为 inner 传出：否则日志里首要错误会显示成
            // "The input is not a valid Base-64 string"，误导成是用户输入的问题而非 Encryption:Key 配置错误。
            throw new InvalidOperationException("Encryption:Key 不是有效的 Base64 编码（需 32 字节 Base64 密钥），请检查 appsettings 的 Encryption:Key 配置。");
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException("Encryption:Key 解码后必须正好是 32 字节，请重新生成 32 字节 Base64 密钥。");
        }

        return key;
    }
}
