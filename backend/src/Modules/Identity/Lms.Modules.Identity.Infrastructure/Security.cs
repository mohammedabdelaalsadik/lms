using Lms.Modules.Identity.Application;
using Lms.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using QRCoder;
using System.Security.Cryptography;
using System.Text;

namespace Lms.Modules.Identity.Infrastructure;

public sealed class JwtOptions
{
    public const string Section = "Identity:Jwt";

    public string Issuer { get; set; } = "lms";
    public string Audience { get; set; } = "lms-web";
    public int LifetimeMinutes { get; set; } = 15;
    public int RefreshLifetimeDays { get; set; } = 14;
    public string SigningKey { get; set; } = "";
}

public sealed class DataProtectionKeys
{
    public const string Section = "Identity:DataProtection";

    /// <summary>Base64 of 32 random bytes, used for AES-256-GCM.</summary>
    public string EncryptionKey { get; set; } = "";

    /// <summary>Base64 of 32 random bytes, used for the HMAC lookup hash.</summary>
    public string LookupKey { get; set; } = "";
}

internal sealed class AesGcmNationalIdProtector : INationalIdProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const string KeyVersion = "v1";

    private readonly byte[] _encryptionKey;
    private readonly byte[] _lookupKey;

    public AesGcmNationalIdProtector(IOptions<DataProtectionKeys> options)
    {
        _encryptionKey = ReadKey(options.Value.EncryptionKey, nameof(options.Value.EncryptionKey));
        _lookupKey = ReadKey(options.Value.LookupKey, nameof(options.Value.LookupKey));
    }

    public ProtectedNationalId Protect(NationalId nationalId)
    {
        var plain = Encoding.UTF8.GetBytes(nationalId.Value);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var stored = $"{KeyVersion}:{Convert.ToBase64String([.. nonce, .. tag, .. cipher])}";
        return new ProtectedNationalId(stored, ComputeLookupHash(nationalId), nationalId.Value[^4..]);
    }

    public string Reveal(ProtectedNationalId nationalId)
    {
        var data = Convert.FromBase64String(nationalId.Cipher[(KeyVersion.Length + 1)..]);
        var plain = new byte[data.Length - NonceSize - TagSize];

        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), plain);
        return Encoding.UTF8.GetString(plain);
    }

    public string ComputeLookupHash(NationalId nationalId) =>
        Convert.ToHexString(HMACSHA256.HashData(_lookupKey, Encoding.UTF8.GetBytes(nationalId.Value)));

    private static byte[] ReadKey(string base64, string name)
    {
        try
        {
            var key = Convert.FromBase64String(base64);
            if (key.Length == 32)
            {
                return key;
            }
        }
        catch (FormatException)
        {
            // Reported below with the same message as a wrong length.
        }

        throw new InvalidOperationException($"{DataProtectionKeys.Section}:{name} must be base64 of 32 bytes.");
    }
}

internal sealed class AspNetPasswordService : IPasswordService
{
    private static readonly object Subject = new();
    private readonly PasswordHasher<object> _hasher = new();
    private readonly Lazy<string> _dummyHash;

    public AspNetPasswordService() =>
        _dummyHash = new Lazy<string>(() => _hasher.HashPassword(Subject, Guid.NewGuid().ToString()));

    public string Hash(string password) => _hasher.HashPassword(Subject, password);

    public bool Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed;

    public void VerifyAgainstDummy(string password) => Verify(_dummyHash.Value, password);
}

public sealed class MfaOptions : IMfaPolicy
{
    public const string Section = "Identity:Mfa";

    public bool RequiredForStaff { get; set; }
}

internal sealed class MfaSecrets : IMfaSecrets
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int RecoveryCodeCount = 8;
    private const string RecoveryAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private readonly byte[] _key;

    public MfaSecrets(IOptions<DataProtectionKeys> options) =>
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Convert.FromBase64String(options.Value.EncryptionKey), 32, info: Encoding.UTF8.GetBytes("mfa-secret"));

    public byte[] NewSecret() => RandomNumberGenerator.GetBytes(Totp.SecretBytes);

    public string Protect(byte[] secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[secret.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, secret, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public byte[] Reveal(string cipher)
    {
        var data = Convert.FromBase64String(cipher);
        var plain = new byte[data.Length - NonceSize - TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), plain);
        return plain;
    }

    public IReadOnlyList<string> NewRecoveryCodes() =>
        [.. Enumerable.Range(0, RecoveryCodeCount).Select(_ => $"{RandomNumberGenerator.GetString(RecoveryAlphabet, 5)}-{RandomNumberGenerator.GetString(RecoveryAlphabet, 5)}")];

    public string HashRecoveryCode(string code)
    {
        var normal = code.Trim().Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normal)));
    }

    public string QrDataUrl(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return $"data:image/png;base64,{Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(6))}";
    }
}

internal sealed class JwtTokenService(IOptions<JwtOptions> options, IOptions<MfaOptions> mfa, TimeProvider clock) : ITokenService
{
    public const string MfaSetupClaim = "mfa_setup";
    private const string ChallengeAudienceSuffix = "-mfa";
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    public string IssueMfaChallenge(User user)
    {
        var jwt = options.Value;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience + ChallengeAudienceSuffix,
            Expires = clock.GetUtcNow().Add(ChallengeLifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(jwt.SigningKey)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { ["sub"] = user.Id.Value.ToString() },
        });
    }

    public async Task<Guid?> ReadMfaChallengeAsync(string challenge)
    {
        var jwt = options.Value;
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(challenge, new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience + ChallengeAudienceSuffix,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(5),
        });

        return result.IsValid && result.Claims.TryGetValue("sub", out var sub) && Guid.TryParse(sub?.ToString(), out var id) ? id : null;
    }

    public AccessToken Issue(User user)
    {
        var jwt = options.Value;
        var expires = clock.GetUtcNow().AddMinutes(jwt.LifetimeMinutes);
        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Id.Value.ToString(),
            ["role"] = user.Role.ToString(),
        };

        // Either duty closes the API until it is done, so one claim serves both.
        if (user.NeedsMfaSetup(mfa.Value.RequiredForStaff) || user.MustChangePassword)
        {
            claims[MfaSetupClaim] = true;
        }

        var value = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256),
            Claims = claims,
        });

        return new AccessToken(value, expires);
    }

    public NewRefreshToken IssueRefreshToken()
    {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expires = clock.GetUtcNow().AddDays(options.Value.RefreshLifetimeDays);
        return new NewRefreshToken(value, HashRefreshToken(value), expires);
    }

    public string HashRefreshToken(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
