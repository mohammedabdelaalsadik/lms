using Lms.SharedKernel;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Lms.Modules.Assessment.Domain;

/// <summary>
/// Safe Exam Browser proves its configuration by sending SHA-256(request URL + Config Key) in a header.
/// The key itself never travels, and each URL gets a different hash, so a copied header is of little use elsewhere.
/// </summary>
public static partial class SafeExam
{
    public static readonly Error InvalidKey = Error.Validation("assessment.seb_invalid_key", "The Config Key must be 64 letters and digits, copied from the SEB configuration tool.");
    public static readonly Error Required = Error.Forbidden("assessment.seb_required", "Open this quiz in Safe Exam Browser using the setup file from your instructor.");

    public static string? Normalize(string? key) => string.IsNullOrWhiteSpace(key) ? null : key.Trim().ToLowerInvariant();

    public static bool IsValidKey(string? key) => key is null || KeyPattern().IsMatch(key);

    public static string Hash(string url, string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url + key)));

    /// <summary>Compared in fixed time, so the answer cannot be guessed one character at a time.</summary>
    public static bool Matches(string url, string key, string? header) =>
        header is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(url, key)), Encoding.ASCII.GetBytes(header.Trim().ToLowerInvariant()));

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex KeyPattern();
}
