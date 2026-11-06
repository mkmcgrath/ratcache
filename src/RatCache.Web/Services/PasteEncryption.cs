using System.Security.Cryptography;
using System.Text;

namespace RatCache.Web.Services;

// NFR-1.2 / NFR-1.3: AES-256-GCM with one server-wide key from configuration.
//
// Key: config "RatCache:EncryptionKey", 32 random bytes, base64.
//   local:  dotnet user-secrets set RatCache:EncryptionKey "$(openssl rand -base64 32)" --project src/RatCache.Web
//   server: environment variable RatCache__EncryptionKey
//
// Stored blob layout: [version=1][12-byte nonce][16-byte tag][ciphertext].
// The paste ID and field name are bound in as associated data, so a ciphertext
// copied onto another paste (or from title to content) fails to decrypt.
public sealed class PasteEncryption
{
    public const string ConfigKey = "RatCache:EncryptionKey";

    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize + TagSize;

    private readonly byte[] _key;

    public PasteEncryption(IConfiguration config)
    {
        var configured = config[ConfigKey];
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                $"{ConfigKey} is not set. Generate one with: dotnet user-secrets set {ConfigKey} \"$(openssl rand -base64 32)\" --project src/RatCache.Web");

        byte[] key;
        try { key = Convert.FromBase64String(configured); }
        catch (FormatException) { throw new InvalidOperationException($"{ConfigKey} must be base64."); }

        if (key.Length != 32)
            throw new InvalidOperationException($"{ConfigKey} must be 32 bytes (256 bits); it is {key.Length}.");
        _key = key;
    }

    public byte[] Encrypt(string plaintext, string pasteId, string field)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var blob = new byte[HeaderSize + plain.Length];
        blob[0] = Version;
        var nonce = blob.AsSpan(1, NonceSize);
        var tag = blob.AsSpan(1 + NonceSize, TagSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, blob.AsSpan(HeaderSize), tag, AssociatedData(pasteId, field));
        return blob;
    }

    public string Decrypt(byte[] blob, string pasteId, string field)
    {
        if (blob.Length < HeaderSize || blob[0] != Version)
            throw new CryptographicException("Unrecognized ciphertext format.");

        var plain = new byte[blob.Length - HeaderSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(blob.AsSpan(1, NonceSize), blob.AsSpan(HeaderSize), blob.AsSpan(1 + NonceSize, TagSize),
            plain, AssociatedData(pasteId, field));
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] AssociatedData(string pasteId, string field) =>
        Encoding.UTF8.GetBytes($"ratcache:v1:{pasteId}:{field}");
}
