using System.Security.Cryptography;
using SwitchPortFinder.Application.Common;

namespace SwitchPortFinder.Application.Import;

/// <summary>
/// Password-protected container for the switch list WITH its SNMP credentials ("backup / move to another server").
/// The secrets never leave the server in clear text: the file is AES-256-GCM encrypted with a key derived from a passphrase (PBKDF2-SHA256, 600,000 iterations,
/// random salt and nonce). Without the passphrase the file is useless, and any change to it is detected.
/// Layout: "SPFENC1" | salt(16) | nonce(12) | tag(16) | ciphertext.
/// </summary>
public static class SecureExport
{
    private static readonly byte[] Magic = "SPFENC1"u8.ToArray();
    private const int SaltLen = 16, NonceLen = 12, TagLen = 16, Iterations = 600_000, MinPassphrase = 12;

    public static string? PassphraseProblem(string? p) => string.IsNullOrEmpty(p) || p.Length < MinPassphrase ? $"The file passphrase must be at least {MinPassphrase} characters." : null;

    private static byte[] Key(string passphrase, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, 32);

    public static byte[] Encrypt(byte[] plain, string passphrase)
    {
        if (PassphraseProblem(passphrase) is { } bad) throw new ValidationFailedException("passphrase", bad);
        var salt = RandomNumberGenerator.GetBytes(SaltLen); var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var cipher = new byte[plain.Length]; var tag = new byte[TagLen];
        using (var aes = new AesGcm(Key(passphrase, salt), TagLen)) aes.Encrypt(nonce, plain, cipher, tag, Magic);
        return [.. Magic, .. salt, .. nonce, .. tag, .. cipher];
    }

    public static bool IsContainer(byte[] data) => data.Length > Magic.Length + SaltLen + NonceLen + TagLen && data.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    /// <summary>Wrong passphrase and a damaged file are deliberately indistinguishable.</summary>
    public static byte[] Decrypt(byte[] data, string? passphrase)
    {
        if (!IsContainer(data)) throw new ValidationFailedException("file", "This is not a SwitchPort Finder encrypted export.");
        if (string.IsNullOrEmpty(passphrase)) throw new ValidationFailedException("passphrase", "Enter the passphrase this file was exported with.");
        var o = Magic.Length; var salt = data[o..(o + SaltLen)]; o += SaltLen; var nonce = data[o..(o + NonceLen)]; o += NonceLen; var tag = data[o..(o + TagLen)]; o += TagLen; var cipher = data[o..];
        var plain = new byte[cipher.Length];
        try { using var aes = new AesGcm(Key(passphrase, salt), TagLen); aes.Decrypt(nonce, cipher, tag, plain, Magic); }
        catch (CryptographicException) { throw new ValidationFailedException("passphrase", "Wrong passphrase, or the file is damaged."); }
        return plain;
    }
}
