using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Lms.Modules.Identity.Domain;

/// <summary>Time-based one-time passwords (RFC 6238), as used by authenticator apps.</summary>
public static class Totp
{
    public const int SecretBytes = 20;
    private const int PeriodSeconds = 30;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / PeriodSeconds;

    public static string Compute(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var value = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7FFFFFFF;

        return (value % 1_000_000).ToString("D6");
    }

    /// <summary>Allows one step of clock drift either way. A step at or before <paramref name="lastUsed"/> is a replay.</summary>
    public static long? Match(byte[] secret, string? code, DateTimeOffset now, long lastUsed)
    {
        code = code?.Trim();

        if (code is not { Length: 6 } || !code.All(char.IsAsciiDigit))
        {
            return null;
        }

        var current = StepAt(now);
        long? matched = null;

        // Every step is checked, so timing does not reveal which one matched.
        for (var step = current - 1; step <= current + 1; step++)
        {
            var same = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(secret, step)), Encoding.ASCII.GetBytes(code));

            if (same && step > lastUsed)
            {
                matched = step;
            }
        }

        return matched;
    }

    public static string ToBase32(byte[] data)
    {
        var text = new StringBuilder();
        var buffer = 0;
        var bits = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;

            while (bits >= 5)
            {
                text.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            text.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return text.ToString();
    }
}
