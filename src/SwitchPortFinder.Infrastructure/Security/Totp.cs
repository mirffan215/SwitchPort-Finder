using System.Security.Cryptography;
using System.Text;

namespace SwitchPortFinder.Infrastructure.Security;

/// <summary>RFC 6238 time-based one-time passwords (HMAC-SHA1, 6 digits, 30 s) - what Microsoft/Google Authenticator, Authy, 1Password etc. implement.</summary>
public static class Totp
{
    public const int Digits = 6, StepSeconds = 30;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);   // 160 bits, as recommended by RFC 4226

    public static string Base32(byte[] data)
    {
        var sb = new StringBuilder(); int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[]? FromBase32(string s)
    {
        var bytes = new List<byte>(); int buffer = 0, bits = 0;
        foreach (var ch in s.Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant())
        {
            var v = Alphabet.IndexOf(ch); if (v < 0) return null;
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return bytes.ToArray();
    }

    public static long StepOf(DateTimeOffset t) => t.ToUnixTimeSeconds() / StepSeconds;

    public static string Code(byte[] secret, long step)
    {
        var msg = new byte[8]; for (var i = 7; i >= 0; i--) { msg[i] = (byte)(step & 0xFF); step >>= 8; }
        var h = HMACSHA1.HashData(secret, msg);
        var o = h[^1] & 0x0F;
        var bin = ((h[o] & 0x7F) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
        return (bin % 1_000_000).ToString("D6");
    }

    /// <summary>Accepts the previous, current and next step (clock drift +/-30 s). Returns the matched step so the caller can refuse a replay of the same code; null when wrong.</summary>
    public static long? Verify(byte[] secret, string? input, DateTimeOffset now, long lastUsedStep = 0)
    {
        var code = new string((input ?? "").Where(char.IsDigit).ToArray());
        if (code.Length != Digits) return null;
        var cur = StepOf(now); long? hit = null;
        for (var s = cur - 1; s <= cur + 1; s++)
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, s)), Encoding.ASCII.GetBytes(code)) && s > lastUsedStep) hit = s;   // no early exit: constant work
        return hit;
    }

    public static string Uri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{System.Uri.EscapeDataString(issuer)}:{System.Uri.EscapeDataString(account)}?secret={Base32(secret)}&issuer={System.Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";

    /// <summary>10 single-use recovery codes like "K7QX-M2PD-9WTA" (60 bits each).</summary>
    public static List<string> NewRecoveryCodes(int count = 10)
    {
        var list = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var raw = Base32(RandomNumberGenerator.GetBytes(8))[..12];
            list.Add($"{raw[..4]}-{raw[4..8]}-{raw[8..12]}");
        }
        return list;
    }

    public static string NormalizeRecovery(string? s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    public static string HashRecovery(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("spf-recovery:" + NormalizeRecovery(code))));
}
