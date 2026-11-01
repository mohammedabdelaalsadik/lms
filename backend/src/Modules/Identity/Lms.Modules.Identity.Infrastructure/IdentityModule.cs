using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Identity.Application;
using Lms.Modules.Identity.Contracts;
using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Lms.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, TimeProvider clock)
    : ModuleDbContext(options, clock), IIdentityUnitOfWork
{
    public const string SchemaName = "identity";

    protected override string Schema => SchemaName;

    public DbSet<User> Users => Set<User>();

    public DbSet<PrivacyRequest> PrivacyRequests => Set<PrivacyRequest>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens");
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).HasConversion(id => id.Value, value => new RefreshTokenId(value)).ValueGeneratedNever();
            b.Property(t => t.UserId).HasConversion(id => id.Value, value => new UserId(value));
            b.Property(t => t.TokenHash).HasMaxLength(64).IsUnicode(false);
            b.HasIndex(t => t.TokenHash).IsUnique();
            b.HasIndex(t => t.FamilyId);
            b.HasIndex(t => t.UserId);
            b.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PrivacyRequest>(b =>
        {
            b.ToTable("PrivacyRequests");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasConversion(id => id.Value, value => new PrivacyRequestId(value)).ValueGeneratedNever();
            b.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            b.Property(r => r.Reason).HasMaxLength(PrivacyRequest.TextMaxLength);
            b.Property(r => r.DecisionNote).HasMaxLength(PrivacyRequest.TextMaxLength);
            b.Ignore(r => r.DownloadUntil);
            b.HasIndex(r => r.UserId);
            b.HasIndex(r => r.Status);

            // The database also holds the one-open-request rule.
            b.HasIndex(r => new { r.UserId, r.Kind }).IsUnique().HasFilter("[Status] = 'Pending'");
        });

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasKey(u => u.Id);
            b.Property(u => u.Id).HasConversion(id => id.Value, value => new UserId(value)).ValueGeneratedNever();
            b.Property(u => u.Email).HasMaxLength(User.EmailMaxLength);
            b.Ignore(u => u.MfaEnabled);
            b.Ignore(u => u.IsErased);
            b.Property(u => u.ResetTokenHash).HasMaxLength(64).IsUnicode(false);
            b.HasIndex(u => u.ResetTokenHash).HasFilter("[ResetTokenHash] IS NOT NULL");
            b.Property(u => u.MfaSecretCipher).HasMaxLength(200).IsUnicode(false);
            b.Property(u => u.MfaRecoveryHashes).HasMaxLength(1000).IsUnicode(false);
            b.HasIndex(u => u.Email).IsUnique();
            b.Property(u => u.PasswordHash).HasMaxLength(500).IsUnicode(false);
            b.Property(u => u.Role).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            b.Property(u => u.Language).HasConversion<string>().HasMaxLength(5).IsUnicode(false);
            b.Property<byte[]>("RowVersion").IsRowVersion();

            b.OwnsOne(u => u.NationalId, n =>
            {
                n.Property(x => x.Cipher).HasColumnName("NationalIdCipher").HasMaxLength(200).IsUnicode(false);
                n.Property(x => x.LookupHash).HasColumnName("NationalIdHash").HasMaxLength(64).IsUnicode(false);
                n.Property(x => x.LastFour).HasColumnName("NationalIdLastFour").HasMaxLength(4).IsUnicode(false);
                n.HasIndex(x => x.LookupHash).IsUnique();
            });
            b.Navigation(u => u.NationalId).IsRequired();

            b.ComplexProperty(u => u.FullName, n =>
            {
                n.Property(x => x.Ar).HasColumnName("FullNameAr").HasMaxLength(User.NameMaxLength);
                n.Property(x => x.En).HasColumnName("FullNameEn").HasMaxLength(User.NameMaxLength);
            });
        });
    }
}

internal sealed class UserRepository(IdentityDbContext db) : IUserRepository
{
    public Task<User?> FindAsync(UserId id, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> FindByResetHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.ResetTokenHash == tokenHash, cancellationToken);

    public Task<User?> FindByLookupHashAsync(string lookupHash, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.NationalId.LookupHash == lookupHash, cancellationToken);

    public Task<bool> LookupHashExistsAsync(string lookupHash, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.NationalId.LookupHash == lookupHash, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Email == email, cancellationToken);

    public void Add(User user) => db.Users.Add(user);

    public async Task<IReadOnlyList<UserOption>> ListByRoleAsync(Role role, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Role == role)
            .OrderBy(u => u.FullName.En)
            .Select(u => new UserOption(u.Id.Value, new NameDto(u.FullName.Ar, u.FullName.En)))
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<UserSummary>> ListAsync(string? search, Role? role, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking();

        if (role is { } wanted)
        {
            query = query.Where(u => u.Role == wanted);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u => u.FullName.Ar.Contains(term) || u.FullName.En.Contains(term) || u.Email.Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserSummary(
                u.Id.Value,
                "******" + u.NationalId.LastFour,
                new NameDto(u.FullName.Ar, u.FullName.En),
                u.Email,
                u.Role,
                u.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<UserSummary>(items, total, page, pageSize);
    }
}

internal sealed class RefreshTokenRepository(IdentityDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindByHashAsync(string hash, CancellationToken cancellationToken) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);

    public async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var family = await db.RefreshTokens.Where(t => t.FamilyId == familyId).ToListAsync(cancellationToken);
        family.ForEach(t => t.Revoke(now));
    }

    public async Task RevokeAllForUserAsync(UserId userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId).ToListAsync(cancellationToken);
        tokens.ForEach(t => t.Revoke(now));
    }
}

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("Lms"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.SchemaName)));

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.Configure<DataProtectionKeys>(configuration.GetSection(DataProtectionKeys.Section));
        services.Configure<MfaOptions>(configuration.GetSection(MfaOptions.Section));
        services.AddSingleton<IMfaPolicy>(sp => sp.GetRequiredService<IOptions<MfaOptions>>().Value);
        services.AddSingleton<IMfaSecrets, MfaSecrets>();

        services.AddScoped<IIdentityUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddSingleton<PresenceTracker>();
        services.AddScoped<IPrivacyRequestRepository, PrivacyRequestRepository>();
        services.AddScoped<IPersonalDataSource, IdentityPersonalData>();
        services.AddHostedService<PrivacyRequestedProcessor>();
        services.AddHostedService<PrivacyRequestDecidedProcessor>();
        services.AddHostedService<UserRegisteredProcessor>();
        services.AddSingleton<INationalIdProtector, AesGcmNationalIdProtector>();
        services.AddSingleton<IPasswordService, AspNetPasswordService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddIdentityApplication();
        services.AddModuleApplication(typeof(IUserRepository).Assembly);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                if (string.IsNullOrEmpty(jwt.SigningKey))
                {
                    throw new InvalidOperationException($"{JwtOptions.Section}:SigningKey is required.");
                }

                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwt.SigningKey)),
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                // The browser holds the token in an HttpOnly cookie, out of reach of page scripts.
                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Cookies.TryGetValue(AuthCookie.Name, out var token))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    /// <summary>Creates a local account from user secrets. Never called outside Development.</summary>
    public static async Task SeedDevelopmentUserAsync(
        this IdentityDbContext db,
        INationalIdProtector protector,
        IPasswordService passwords,
        TimeProvider clock,
        string nationalId,
        string password,
        LocalizedText name,
        string email,
        Role role)
    {
        var id = NationalId.Create(nationalId).Value;
        var hash = protector.ComputeLookupHash(id);

        if (await db.Users.AnyAsync(u => u.NationalId.LookupHash == hash))
        {
            return;
        }

        db.Users.Add(User.Register(protector.Protect(id), name, email, passwords.Hash(password), role, Language.Ar, clock.GetUtcNow()));
        await db.SaveChangesAsync();
    }
}
