using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Catalog.Application;
using Lms.Modules.Catalog.Contracts;
using Lms.Modules.Catalog.Domain;
using Lms.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Modules.Catalog.Infrastructure;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, TimeProvider clock)
    : ModuleDbContext(options, clock), ICatalogUnitOfWork
{
    public const string SchemaName = "catalog";

    protected override string Schema => SchemaName;

    public DbSet<Course> Courses => Set<Course>();

    public DbSet<CourseCategory> Categories => Set<CourseCategory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CourseCategory>(b =>
        {
            b.ToTable("Categories");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion(id => id.Value, value => new CourseCategoryId(value)).ValueGeneratedNever();
            b.HasIndex(c => c.ParentId);
            b.ComplexProperty(c => c.Name, t =>
            {
                t.Property(x => x.Ar).HasColumnName("NameAr").HasMaxLength(CourseCategory.NameMaxLength);
                t.Property(x => x.En).HasColumnName("NameEn").HasMaxLength(CourseCategory.NameMaxLength);
            });
        });

        modelBuilder.Entity<Course>(b =>
        {
            b.ToTable("Courses");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion(id => id.Value, value => new CourseId(value)).ValueGeneratedNever();
            b.Property(c => c.Code).HasMaxLength(Course.CodeMaxLength).IsUnicode(false);
            b.HasIndex(c => c.Code).IsUnique();
            b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            b.HasIndex(c => c.Status);
            b.Property<byte[]>("RowVersion").IsRowVersion();

            b.ComplexProperty(c => c.Title, t =>
            {
                t.Property(x => x.Ar).HasColumnName("TitleAr").HasMaxLength(Course.TitleMaxLength);
                t.Property(x => x.En).HasColumnName("TitleEn").HasMaxLength(Course.TitleMaxLength);
            });
            b.ComplexProperty(c => c.Description, t =>
            {
                t.Property(x => x.Ar).HasColumnName("DescriptionAr").HasMaxLength(Course.DescriptionMaxLength);
                t.Property(x => x.En).HasColumnName("DescriptionEn").HasMaxLength(Course.DescriptionMaxLength);
            });
        });
    }
}

internal sealed class CategoryRepository(CatalogDbContext db) : ICategoryRepository
{
    public async Task<IReadOnlyList<CourseCategory>> ListAsync(CancellationToken cancellationToken) =>
        await db.Categories.AsNoTracking().OrderBy(c => c.Name.En).ToListAsync(cancellationToken);

    public Task<CourseCategory?> FindAsync(CourseCategoryId id, CancellationToken cancellationToken) =>
        db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> HasCoursesAsync(Guid categoryId, CancellationToken cancellationToken) =>
        db.Courses.AnyAsync(c => c.CategoryId == categoryId, cancellationToken);

    public void Add(CourseCategory category) => db.Categories.Add(category);

    public void Remove(CourseCategory category) => db.Categories.Remove(category);
}

internal sealed class CourseRepository(CatalogDbContext db) : ICourseRepository
{
    public Task<Course?> FindAsync(CourseId id, CancellationToken cancellationToken) =>
        db.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        db.Courses.AnyAsync(c => c.Code == code, cancellationToken);

    public void Add(Course course) => db.Courses.Add(course);

    public async Task<PagedResult<CourseResponse>> ListAsync(CourseFilter filter, CancellationToken cancellationToken)
    {
        var query = db.Courses.AsNoTracking();

        if (filter.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(c => c.Code.Contains(term) || c.Title.Ar.Contains(term) || c.Title.En.Contains(term));
        }

        if (filter.CategoryIds is { } ids)
        {
            query = query.Where(c => c.CategoryId != null && ids.Contains(c.CategoryId.Value));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(c => new CourseResponse(
                c.Id.Value,
                c.Code,
                new LocalizedTextDto(c.Title.Ar, c.Title.En),
                new LocalizedTextDto(c.Description.Ar, c.Description.En),
                c.DurationHours,
                c.Status,
                c.CreatedAt,
                c.PublishedAt,
                c.CategoryId))
            .ToListAsync(cancellationToken);

        return new PagedResult<CourseResponse>(items, total, filter.Page, filter.PageSize);
    }
}

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CatalogDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("Lms"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", CatalogDbContext.SchemaName)));

        services.AddScoped<ICatalogUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());
        services.AddScoped<ICourseRepository, CourseRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICourseCatalog, CourseCatalog>();
        services.AddModuleApplication(typeof(ICourseRepository).Assembly);
        return services;
    }

    /// <summary>Creates sample courses for local development. All content is made up.</summary>
    public static async Task SeedDevelopmentDataAsync(this CatalogDbContext db, TimeProvider clock)
    {
        if (await db.Courses.AnyAsync())
        {
            return;
        }

        var samples = new (string Code, string TitleAr, string TitleEn, string DescAr, string DescEn, int Hours, bool Publish)[]
        {
            ("SAF-101", "أساسيات السلامة المهنية", "Occupational Safety Basics", "مقدمة في مخاطر بيئة العمل وطرق الوقاية منها.", "An introduction to workplace hazards and how to prevent them.", 24, true),
            ("LED-210", "مهارات القيادة للمشرفين", "Leadership Skills for Supervisors", "بناء الفرق وإدارة الأداء والتواصل الفعال.", "Team building, performance management and clear communication.", 32, true),
            ("DAT-120", "تحليل البيانات باستخدام الجداول", "Data Analysis with Spreadsheets", "تنظيف البيانات وتلخيصها وعرضها بوضوح.", "Clean, summarize and present data clearly.", 18, true),
            ("CUS-105", "التميز في خدمة العملاء", "Customer Service Excellence", "فهم احتياجات العملاء والتعامل مع الشكاوى.", "Understand customer needs and handle complaints well.", 12, false),
        };

        foreach (var s in samples)
        {
            var course = Course.Create(
                s.Code,
                new LocalizedText(s.TitleAr, s.TitleEn),
                new LocalizedText(s.DescAr, s.DescEn),
                s.Hours,
                clock.GetUtcNow()).Value;

            if (s.Publish)
            {
                course.Publish(clock.GetUtcNow());
            }

            db.Courses.Add(course);
        }

        await db.SaveChangesAsync();
    }
}
