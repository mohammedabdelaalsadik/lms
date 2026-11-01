using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Learning.Application;
using Lms.Modules.Learning.Contracts;
using Lms.Modules.Learning.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Modules.Learning.Infrastructure;

public sealed class LearningDbContext(DbContextOptions<LearningDbContext> options, TimeProvider clock)
    : ModuleDbContext(options, clock), ILearningUnitOfWork
{
    public const string SchemaName = "learning";

    protected override string Schema => SchemaName;

    public DbSet<CourseContent> Contents => Set<CourseContent>();

    public DbSet<CourseProgress> Progress => Set<CourseProgress>();

    public DbSet<ScormAttempt> ScormAttempts => Set<ScormAttempt>();

    public DbSet<BinItem> BinItems => Set<BinItem>();

    public DbSet<LiveClass> LiveClasses => Set<LiveClass>();

    public DbSet<LiveAttendance> LiveAttendance => Set<LiveAttendance>();

    public DbSet<PrivateFile> PrivateFiles => Set<PrivateFile>();

    public DbSet<GlossaryEntry> GlossaryEntries => Set<GlossaryEntry>();

    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();

    public DbSet<FormResponse> FormResponses => Set<FormResponse>();

    public DbSet<AttendanceSession> AttendanceSessions => Set<AttendanceSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CourseContent>(b =>
        {
            b.ToTable("CourseContents");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion(id => id.Value, value => new CourseContentId(value)).ValueGeneratedNever();
            b.HasIndex(c => c.CourseId).IsUnique();
            b.Property<byte[]>("RowVersion").IsRowVersion();
            b.HasMany(c => c.Sections).WithOne().HasForeignKey("CourseContentId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(c => c.Sections).HasField("_sections").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Section>(b =>
        {
            b.ToTable("Sections");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasConversion(id => id.Value, value => new SectionId(value)).ValueGeneratedNever();
            b.ComplexProperty(s => s.Title, t =>
            {
                t.Property(x => x.Ar).HasColumnName("TitleAr").HasMaxLength(Section.TitleMaxLength);
                t.Property(x => x.En).HasColumnName("TitleEn").HasMaxLength(Section.TitleMaxLength);
            });
            b.HasMany(s => s.Activities).WithOne().HasForeignKey("SectionId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(s => s.Activities).HasField("_activities").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Activity>(b =>
        {
            b.ToTable("Activities");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasConversion(id => id.Value, value => new ActivityId(value)).ValueGeneratedNever();
            b.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            b.Property(a => a.BodyAr).HasMaxLength(Activity.BodyMaxLength);
            b.Property(a => a.BodyEn).HasMaxLength(Activity.BodyMaxLength);
            b.Property(a => a.VideoUrl).HasMaxLength(Activity.UrlMaxLength);
            b.Property(a => a.FileKey).HasMaxLength(64).IsUnicode(false);
            b.Property(a => a.FileName).HasMaxLength(255);
            b.Property(a => a.FileContentType).HasMaxLength(120).IsUnicode(false);
            b.ComplexProperty(a => a.Title, t =>
            {
                t.Property(x => x.Ar).HasColumnName("TitleAr").HasMaxLength(Activity.TitleMaxLength);
                t.Property(x => x.En).HasColumnName("TitleEn").HasMaxLength(Activity.TitleMaxLength);
            });
        });

        modelBuilder.Entity<FormSubmission>(b =>
        {
            b.ToTable("FormSubmissions");
            b.HasKey(s => s.Id);
            b.Property(s => s.ActivityId).HasConversion(id => id.Value, value => new ActivityId(value));
            b.HasIndex(s => new { s.ActivityId, s.CohortId, s.TraineeId }).IsUnique();
            b.HasIndex(s => new { s.CohortId, s.TraineeId });
        });

        modelBuilder.Entity<FormResponse>(b =>
        {
            b.ToTable("FormResponses");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.ActivityId).HasConversion(id => id.Value, value => new ActivityId(value));
            b.HasIndex(r => new { r.CohortId, r.ActivityId });
            b.HasIndex(r => r.TraineeId);
        });

        modelBuilder.Entity<GlossaryEntry>(b =>
        {
            b.ToTable("GlossaryEntries");
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.CourseId);
            b.HasIndex(e => e.AuthorId);
            b.ComplexProperty(e => e.Term, t =>
            {
                t.Property(x => x.Ar).HasColumnName("TermAr").HasMaxLength(GlossaryEntry.TermMaxLength);
                t.Property(x => x.En).HasColumnName("TermEn").HasMaxLength(GlossaryEntry.TermMaxLength);
            });
            b.ComplexProperty(e => e.Definition, t =>
            {
                t.Property(x => x.Ar).HasColumnName("DefinitionAr").HasMaxLength(GlossaryEntry.DefinitionMaxLength);
                t.Property(x => x.En).HasColumnName("DefinitionEn").HasMaxLength(GlossaryEntry.DefinitionMaxLength);
            });
        });

        modelBuilder.Entity<PrivateFile>(b =>
        {
            b.ToTable("PrivateFiles");
            b.HasKey(f => f.Id);
            b.Property(f => f.StorageKey).HasMaxLength(64).IsUnicode(false);
            b.Property(f => f.FileName).HasMaxLength(255);
            b.Property(f => f.ContentType).HasMaxLength(120).IsUnicode(false);
            b.HasIndex(f => f.OwnerId);
        });

        modelBuilder.Entity<LiveClass>(b =>
        {
            b.ToTable("LiveClasses");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion(id => id.Value, value => new LiveClassId(value)).ValueGeneratedNever();
            b.Property(c => c.MeetingId).HasMaxLength(64).IsUnicode(false);
            b.HasIndex(c => c.CohortId);
            b.ComplexProperty(c => c.Title, t =>
            {
                t.Property(x => x.Ar).HasColumnName("TitleAr").HasMaxLength(LiveClass.TitleMaxLength);
                t.Property(x => x.En).HasColumnName("TitleEn").HasMaxLength(LiveClass.TitleMaxLength);
            });
        });

        modelBuilder.Entity<LiveAttendance>(b =>
        {
            b.ToTable("LiveAttendance");
            b.HasKey(a => a.Id);
            b.Property(a => a.LiveClassId).HasConversion(id => id.Value, value => new LiveClassId(value));
            b.HasIndex(a => new { a.LiveClassId, a.TraineeId }).IsUnique();
            b.HasIndex(a => a.TraineeId);
        });

        modelBuilder.Entity<BinItem>(b =>
        {
            b.ToTable("RecycleBin");
            b.HasKey(i => i.Id);
            b.Property(i => i.Kind).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            b.Property(i => i.TitleAr).HasMaxLength(Activity.TitleMaxLength);
            b.Property(i => i.TitleEn).HasMaxLength(Activity.TitleMaxLength);
            b.Ignore(i => i.ExpiresAt);
            b.HasIndex(i => new { i.CourseId, i.RemovedAt });
            b.HasIndex(i => i.RemovedAt);
        });

        modelBuilder.Entity<ScormAttempt>(b =>
        {
            b.ToTable("ScormAttempts");
            b.HasKey(a => a.Id);
            b.Property(a => a.ActivityId).HasConversion(id => id.Value, value => new ActivityId(value));
            b.HasIndex(a => new { a.ActivityId, a.CohortId, a.TraineeId }).IsUnique();
        });

        modelBuilder.Entity<CourseProgress>(b =>
        {
            b.ToTable("CourseProgress");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).HasConversion(id => id.Value, value => new CourseProgressId(value)).ValueGeneratedNever();
            b.Property(p => p.FinalGradePercent).HasPrecision(5, 2);
            b.HasIndex(p => new { p.CohortId, p.TraineeId }).IsUnique();
            b.HasIndex(p => p.TraineeId);
            b.Property<byte[]>("RowVersion").IsRowVersion();

            b.OwnsMany(p => p.Completions, c =>
            {
                c.ToTable("ActivityCompletions");
                c.WithOwner().HasForeignKey("CourseProgressId");
                c.Property(x => x.ActivityId).HasConversion(id => id.Value, value => new ActivityId(value));
                c.HasKey("CourseProgressId", nameof(ActivityCompletion.ActivityId));
            });
            b.Navigation(p => p.Completions).HasField("_completions").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<AttendanceSession>(b =>
        {
            b.ToTable("AttendanceSessions");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasConversion(id => id.Value, value => new AttendanceSessionId(value)).ValueGeneratedNever();
            b.HasIndex(s => new { s.CohortId, s.Date }).IsUnique();
            b.Property<byte[]>("RowVersion").IsRowVersion();

            b.OwnsMany(s => s.Records, r =>
            {
                r.ToTable("AttendanceRecords");
                r.WithOwner().HasForeignKey("AttendanceSessionId");
                r.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
                r.HasKey("AttendanceSessionId", nameof(AttendanceRecord.TraineeId));
            });
            b.Navigation(s => s.Records).HasField("_records").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}

internal sealed class CourseContentRepository(LearningDbContext db) : ICourseContentRepository
{
    private IQueryable<CourseContent> Full => db.Contents.Include(c => c.Sections).ThenInclude(s => s.Activities).AsSplitQuery();

    public Task<CourseContent?> FindByCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        Full.FirstOrDefaultAsync(c => c.CourseId == courseId, cancellationToken);

    public Task<CourseContent?> FindByActivityAsync(ActivityId activityId, CancellationToken cancellationToken) =>
        Full.FirstOrDefaultAsync(c => c.Sections.Any(s => s.Activities.Any(a => a.Id == activityId)), cancellationToken);

    public async Task<IReadOnlyList<CourseContent>> ListByCoursesAsync(IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken) =>
        await Full.AsNoTracking().Where(c => courseIds.Contains(c.CourseId)).ToListAsync(cancellationToken);

    public void Add(CourseContent content) => db.Contents.Add(content);
}

internal sealed class ScormAttemptRepository(LearningDbContext db) : IScormAttemptRepository
{
    public Task<ScormAttempt?> FindAsync(ActivityId activityId, Guid cohortId, Guid traineeId, CancellationToken cancellationToken) =>
        db.ScormAttempts.FirstOrDefaultAsync(a => a.ActivityId == activityId && a.CohortId == cohortId && a.TraineeId == traineeId, cancellationToken);

    public void Add(ScormAttempt attempt) => db.ScormAttempts.Add(attempt);
}

/// <summary>Signs with a key derived for this purpose alone, so a link can never pass as a session.</summary>
internal sealed class ScormTokens(IConfiguration configuration, TimeProvider clock) : IScormTokens
{
    private const int PayloadSize = (3 * 16) + 8;
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private readonly byte[] _key = HKDF.DeriveKey(
        HashAlgorithmName.SHA256,
        Convert.FromBase64String(configuration["Identity:Jwt:SigningKey"] ?? throw new InvalidOperationException("Identity:Jwt:SigningKey is required.")),
        32,
        info: Encoding.UTF8.GetBytes("scorm-link"));

    public string Issue(ScormLaunch launch)
    {
        var expires = clock.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds();
        byte[] payload = [.. launch.ActivityId.ToByteArray(), .. launch.CohortId.ToByteArray(), .. launch.TraineeId.ToByteArray(), .. BitConverter.GetBytes(expires)];
        return Base64Url.EncodeToString([.. payload, .. HMACSHA256.HashData(_key, payload)]);
    }

    public ScormLaunch? Read(string token)
    {
        byte[] data;

        try
        {
            data = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            return null;
        }

        if (data.Length != PayloadSize + 32)
        {
            return null;
        }

        var payload = data[..PayloadSize];

        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_key, payload), data[PayloadSize..])
            || BitConverter.ToInt64(payload, 48) < clock.GetUtcNow().ToUnixTimeSeconds())
        {
            return null;
        }

        return new ScormLaunch(new Guid(payload[..16]), new Guid(payload[16..32]), new Guid(payload[32..48]));
    }
}

internal sealed class CohortProgressStats(LearningDbContext db) : ICohortProgressStats
{
    public async Task<IReadOnlyDictionary<Guid, CohortProgressStat>> GetAsync(IReadOnlyCollection<Guid> cohortIds, CancellationToken cancellationToken)
    {
        var rows = await db.Progress.AsNoTracking()
            .Where(p => cohortIds.Contains(p.CohortId))
            .GroupBy(p => p.CohortId)
            .Select(g => new { CohortId = g.Key, Completed = g.Count(p => p.CompletedAt != null), Average = g.Average(p => p.FinalGradePercent) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.CohortId, r => new CohortProgressStat(r.Completed, r.Average is { } value ? Math.Round(value, 1) : null));
    }
}

internal sealed class CourseProgressRepository(LearningDbContext db) : ICourseProgressRepository
{
    public async Task<CourseProgress?> FindAsync(Guid cohortId, Guid traineeId, CancellationToken cancellationToken) =>
        db.Progress.Local.FirstOrDefault(p => p.CohortId == cohortId && p.TraineeId == traineeId)
        ?? await db.Progress.FirstOrDefaultAsync(p => p.CohortId == cohortId && p.TraineeId == traineeId, cancellationToken);

    public async Task<IReadOnlyList<CourseProgress>> ListByCohortAsync(Guid cohortId, CancellationToken cancellationToken) =>
        await db.Progress.AsNoTracking().Where(p => p.CohortId == cohortId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CourseProgress>> ListByTraineeAsync(Guid traineeId, CancellationToken cancellationToken) =>
        await db.Progress.AsNoTracking().Where(p => p.TraineeId == traineeId).ToListAsync(cancellationToken);

    public void Add(CourseProgress progress) => db.Progress.Add(progress);
}

internal sealed class AttendanceRepository(LearningDbContext db) : IAttendanceRepository
{
    public Task<AttendanceSession?> FindAsync(Guid cohortId, DateOnly date, CancellationToken cancellationToken) =>
        db.AttendanceSessions.FirstOrDefaultAsync(s => s.CohortId == cohortId && s.Date == date, cancellationToken);

    public async Task<IReadOnlyList<AttendanceSession>> ListByCohortAsync(Guid cohortId, CancellationToken cancellationToken) =>
        await db.AttendanceSessions.AsNoTracking().Where(s => s.CohortId == cohortId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, AttendanceTally>> TallyAsync(Guid cohortId, CancellationToken cancellationToken)
    {
        var rows = await db.AttendanceSessions.AsNoTracking()
            .Where(s => s.CohortId == cohortId)
            .SelectMany(s => s.Records)
            .GroupBy(r => r.TraineeId)
            .Select(g => new
            {
                TraineeId = g.Key,
                Present = g.Count(r => r.Status == AttendanceStatus.Present),
                Excused = g.Count(r => r.Status == AttendanceStatus.Excused),
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.TraineeId, r => new AttendanceTally(r.Present, r.Excused));
    }

    public void Add(AttendanceSession session) => db.AttendanceSessions.Add(session);
}

public static class LearningModule
{
    public static IServiceCollection AddLearningModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<LearningDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("Lms"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LearningDbContext.SchemaName)));

        services.AddScoped<ILearningUnitOfWork>(sp => sp.GetRequiredService<LearningDbContext>());
        services.AddScoped<ICourseContentRepository, CourseContentRepository>();
        services.AddScoped<ICourseProgressRepository, CourseProgressRepository>();
        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<ICourseCompletion, CourseCompletionService>();
        services.AddHostedService<CourseCompletedProcessor>();
        services.AddHostedService<AttendanceCorrectedProcessor>();
        services.AddScoped<LearningAccess>();
        services.AddScoped<ICohortProgressStats, CohortProgressStats>();
        services.AddScoped<IScormAttemptRepository, ScormAttemptRepository>();
        services.AddScoped<ActivityCompleter>();
        services.AddScoped<ILiveClassRepository, LiveClassRepository>();
        services.AddScoped<LiveClassAccess>();
        services.AddScoped<ILiveClassSchedule, LiveClassSchedule>();
        services.AddScoped<LiveClassOpener>();
        services.AddSingleton<ILiveClassServer, BigBlueButtonServer>();
        services.AddHostedService<LiveClassScheduledProcessor>();
        services.AddScoped<IPrivateFileRepository, PrivateFileRepository>();
        services.AddScoped<IGlossaryRepository, GlossaryRepository>();
        services.AddScoped<GlossaryGate>();
        services.AddScoped<IFormRepository, FormRepository>();
        services.AddScoped<IRecycleBinRepository, RecycleBinRepository>();
        services.AddScoped<RecycleBin>();
        services.AddHostedService<RecycleBinPurge>();
        services.AddSingleton<IScormTokens, ScormTokens>();
        services.AddScoped<ContentEditor>();
        services.AddScoped<TraineeGate>();
        services.AddScoped<Lms.BuildingBlocks.Application.IPersonalDataSource, LearningPersonalData>();
        services.AddModuleApplication(typeof(ICourseContentRepository).Assembly);
        return services;
    }
}
