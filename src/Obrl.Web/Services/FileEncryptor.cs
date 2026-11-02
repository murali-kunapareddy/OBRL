using System.Security.Cryptography;
using System.Text;

namespace Obrl.Web.Services;

/// <summary>
/// AES-256-GCM encryption for every file OBRL stores. File layout:
/// "OBRL1" (5 bytes) | nonce (12) | tag (16) | ciphertext.
/// </summary>
public sealed class FileEncryptor
{
    private static readonly byte[] Magic = "OBRL1"u8.ToArray();
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public FileEncryptor(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("Master key must be 32 bytes.", nameof(key));
        _key = key;
    }

    /// <summary>Key used to sign short-lived reader URLs, derived from the master key.</summary>
    public byte[] DeriveKey(string purpose) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, info: Encoding.UTF8.GetBytes(purpose));

    public byte[] Encrypt(ReadOnlySpan<byte> plain)
    {
        var output = new byte[Magic.Length + NonceSize + TagSize + plain.Length];
        Magic.CopyTo(output, 0);
        var nonce = output.AsSpan(Magic.Length, NonceSize);
        var tag = output.AsSpan(Magic.Length + NonceSize, TagSize);
        var cipher = output.AsSpan(Magic.Length + NonceSize + TagSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        return output;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> data)
    {
        if (data.Length < Magic.Length + NonceSize + TagSize || !data[..Magic.Length].SequenceEqual(Magic))
            throw new CryptographicException("Not an OBRL encrypted file.");
        var nonce = data.Slice(Magic.Length, NonceSize);
        var tag = data.Slice(Magic.Length + NonceSize, TagSize);
        var cipher = data[(Magic.Length + NonceSize + TagSize)..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    /// <summary>Loads the master key, creating it (owner-only permissions) if the file does not exist.</summary>
    public static byte[] LoadOrCreateKey(string keyFile, ILogger logger)
    {
        if (File.Exists(keyFile))
        {
            var key = Convert.FromBase64String(File.ReadAllText(keyFile).Trim());
            if (key.Length != 32) throw new InvalidOperationException($"Master key in {keyFile} is not 32 bytes.");
            return key;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyFile))!);
        var newKey = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(keyFile, Convert.ToBase64String(newKey));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        logger.LogWarning("Created a new master key at {KeyFile}. Back it up: without it, stored books cannot be read.", keyFile);
        return newKey;
    }
}
