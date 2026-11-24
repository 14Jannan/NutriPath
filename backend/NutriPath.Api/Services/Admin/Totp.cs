using System.Security.Cryptography;
using System.Text;

namespace NutriPath.Api.Services.Admin;

/// <summary>
/// RFC 6238 time-based one-time passwords (HMAC-SHA1, 30-second steps, 6 digits), compatible with
/// Google Authenticator, Microsoft Authenticator, 1Password and similar apps.
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string NewSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public static long TimeStep(DateTimeOffset now) => now.ToUnixTimeSeconds() / StepSeconds;

    public static string Code(string base32Secret, long step)
    {
        var key = Base32Decode(base32Secret);
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(step & 0xFF);
            step >>= 8;
        }
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// Returns the matched time step (current ±1 for clock drift) or null. Steps at or below
    /// <paramref name="lastUsedStep"/> are refused, so a code can't be used twice.
    /// </summary>
    public static long? Verify(string base32Secret, string? code, DateTimeOffset now, long lastUsedStep)
    {
        if (code is null) return null;
        code = code.Replace(" ", "");
        if (code.Length != 6 || !code.All(char.IsAsciiDigit)) return null;
        var current = TimeStep(now);
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (step <= lastUsedStep) continue;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(base32Secret, step)),
                    Encoding.ASCII.GetBytes(code)))
                return step;
        }
        return null;
    }

    public static string OtpAuthUri(string issuer, string account, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period={StepSeconds}";

    public static string Base32Encode(byte[] data)
    {
        var builder = new StringBuilder((data.Length + 4) / 5 * 8);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) builder.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return builder.ToString();
    }

    public static byte[] Base32Decode(string input)
    {
        var clean = input.TrimEnd('=').Replace(" ", "").ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Base32Alphabet.IndexOf(c);
            if (value < 0) throw new FormatException("Invalid base32 character.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return [.. output];
    }
}
