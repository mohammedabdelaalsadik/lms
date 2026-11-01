using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Assessment.Application;
using Lms.Modules.Assessment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Modules.Assessment.Infrastructure;

public sealed class AssessmentDbContext(DbContextOptions<AssessmentDbContext> options, TimeProvider clock)
    : ModuleDbContext(options, clock), IAssessmentUnitOfWork
{
    public const string SchemaName = "assessment";

    protected override string Schema => SchemaName;

    public DbSet<Quiz> Quizzes => Set<Quiz>();

    public DbSet<QuizAttempt> Attempts => Set<QuizAttempt>();

    public DbSet<Assignment> Assignments => Set<Assignment>();

    public DbSet<AssignmentSubmission> Submissions => Set<AssignmentSubmission>();

    public DbSet<Gradebook> Gradebooks => Set<Gradebook>();

    public DbSet<BankQuestion> BankQuestions => Set<BankQuestion>();

    public DbSet<Workshop> Workshops => Set<Workshop>();

    public DbSet<WorkshopSubmission> WorkshopSubmissions => Set<WorkshopSubmission>();

    public DbSet<WorkshopReview> WorkshopReviews => Set<WorkshopReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Workshop>(b =>
        {
            b.ToTable("Workshops");
            b.HasKey(w => w.Id);
            b.Property(w => w.Id).HasConversion(id => id.Value, value => new WorkshopId(value)).ValueGeneratedNever();
            b.Property(w => w.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            b.HasIndex(w => w.CohortId);
            b.ComplexProperty(w => w.Title, t =>
            {
                t.Property(x => x.Ar).HasColumnName("TitleAr").HasMaxLength(Workshop.TitleMaxLength);
                t.Property(x => x.En).HasColumnName("TitleEn").HasMaxLength(Workshop.TitleMaxLength);
            });
            b.ComplexProperty(w => w.Instructions, t =>
            {
                t.Property(x => x.Ar).HasColumnName("InstructionsAr").HasMaxLength(Workshop.InstructionsMaxLength);
                t.Property(x => x.En).HasColumnName("InstructionsEn").HasMaxLength(Workshop.InstructionsMaxLength);
            });
        });

        modelBuilder.Entity<WorkshopSubmission>(b =>
        {
            b.ToTable("WorkshopSubmissions");
            b.HasKey(s => s.Id);
            b.Property(s => s.WorkshopId).HasConversion(id => id.Value, value => new WorkshopId(value));
            b.Property(s => s.Text).HasMaxLength(Workshop.TextMaxLength);
            b.HasIndex(s => new { s.WorkshopId, s.TraineeId }).IsUnique();
        });

        modelBuilder.Entity<WorkshopReview>(b =>
        {
            b.ToTable("WorkshopReviews");
            b.HasKey(r => r.Id);
            b.Property(r => r.WorkshopId).HasConversion(id => id.Value, value => new WorkshopId(value));
            b.Property(r => r.Score).HasPrecision(5, 2);
            b.Property(r => r.Comment).HasMaxLength(Workshop.CommentMaxLength);
            b.HasIndex(r => new { r.SubmissionId, r.ReviewerId }).IsUnique();
            b.HasIndex(r => r.WorkshopId);
            b.HasIndex(r => r.ReviewerId);
        });

        modelBuilder.Entity<Quiz>(b =>
        {
            b.Property(q => q.ExamKey).HasMaxLength(64).IsUnicode(false);
            b.ToTable("Quizzes");
            b.HasKey(q => q.Id);
            b.Property(q => q.Id).HasConversion(id => id.Value, value => new QuizId(value)).ValueGeneratedNever();
            b.Property(q => q.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            b.HasIndex(q => q.CohortId);
            b.Property<byte[]>("RowVersion").IsRowVersion();
            b.ComplexProperty(q => q.Title, t =>
            {
                t.Property(x => x.Ar).HasColumnName("TitleAr").HasMaxLength(Quiz.TitleMaxLength);
                t.Property(x => x.En).HasColumnName("TitleEn").HasMaxLength(Quiz.TitleMaxLength);
            });
            b.HasMany(q => q.Questions).WithOne().HasForeignKey("QuizId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(q => q.Questions).HasField("_questions").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<QuizQuestion>(b =>
        {
            b.ToTable("Questions");
            b.HasKey(q => q.Id);
            b.Property(q => q.Id).HasConversion(id => id.Value, value => new QuestionId(value)).ValueGeneratedNever();
            b.Property(q => q.Kind).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            b.Property(q => q.Tolerance).HasPrecision(18, 6);
            b.ComplexProperty(q => q.Text, t =>
            {
                t.Property(x => x.Ar).HasColumnName("TextAr").HasMaxLength(QuizQuestion.TextMaxLength);
                t.Property(x => x.En).HasColumnName("TextEn").HasMaxLength(QuizQuestion.TextMaxLength);
            });
            b.HasMany(q => q.Options).WithOne().HasForeignKey("QuestionId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(q => q.Options).HasField("_options").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<QuizOption>(b =>
        {
            b.ToTable("Options");
            b.HasKey(o => o.Id);
            b.Property(o => o.MatchAr).HasMaxLength(QuizOption.TextMaxLength);
            b.Property(o => o.MatchEn).HasMaxLength(QuizOption.TextMaxLength);
            b.Property(o => o.Id).HasConversion(id => id.Value, value => new OptionId(value)).ValueGeneratedNever();
            b.ComplexProperty(o => o.Text, t =>
            {
                t.Property(x => x.Ar).HasColumnName("TextAr").HasMaxLength(QuizOption.TextMaxLength);
                t.Property(x => x.En).HasColumnName("TextEn").HasMaxLength(QuizOption.TextMaxLength);
            });
        });

        modelBuilder.Entity<QuizAttempt>(b =>
        {
            b.ToTable("Attempts");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasConversion(id => id.Value, value => new AttemptId(value)).ValueGeneratedNever();
            b.Property(a => a.QuizId).HasConversion(id => id.Value, value => new QuizId(value));
            b.Property(a => a.ScorePercent).HasPrecision(5, 2);
            b.Property(a => a.QuestionIds).IsUnicode(false);
            b.Property<byte[]>("RowVersion").IsRowVersion();
            b.HasIndex(a => new { a.QuizId, a.TraineeId, a.Number }).IsUnique();

            // Two tabs cannot open two attempts at once.
            b.HasIndex(a => new { a.QuizId, a.TraineeId }).IsUnique().HasFilter("[SubmittedAt] IS NULL");

            b.OwnsMany(a => a.Answers, x =>
            {
                x.ToTable("AttemptAnswers");
                x.WithOwner().HasForeignKey("AttemptId");
                x.Property(y => y.QuestionId).HasConversion(id => id.Value, value => new QuestionId(value));
                x.Property(y => y.Response).HasMaxLength(QuizQuestion.MaxEssayLength);
                x.Property(y => y.AwardedPoints).HasPrecision(7, 2);
                x.HasKey("AttemptId", nameof(AttemptAnswer.QuestionId));
            });
            b.Navigation(a => a.Answers).HasField("_answers").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<BankQuestion>(b =>
        {
            b.ToTable("BankQuestions");
            b.HasKey(q => q.Id);
            b.Property(q => q.Id).HasConversion(id => id.Value, value => new BankQuestionId(value)).ValueGeneratedNever();
            b.Property(q => q.Kind).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            b.Property(q => q.Category).HasMaxLength(BankQuestion.CategoryMaxLength);
            b.Property(q => q.Tolerance).HasPrecision(18, 6);
            b.Property<byte[]>("RowVersion").IsRowVersion();
            b.HasIndex(q => new { q.CourseId, q.Category });
            b.ComplexProperty(q => q.Text, t =>
            {
                t.Property(x => x.Ar).HasColumnName("TextAr").HasMaxLength(QuizQuestion.TextMaxLength);
                t.Property(x => x.En).HasColumnName("TextEn").HasMaxLength(QuizQuestion.TextMaxLength);
            });
            b.Property(q => q.Options).HasConversion(
                options => JsonSerializer.Serialize(options, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<OptionDraft>>(json, (JsonSerializerOptions?)null) ?? new List<OptionDraft>(),
                new ValueComparer<IReadOnlyList<OptionDraft>>(
                    (a, b) => a != null && b != null && a.SequenceEqual(b),
                    list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                    list => list.ToList()));
        });

        modelBuilder.Entity<Assignment>(b =>
        {
            b.ToTable("Assignments");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasConversion(id => id.Value, value => new AssignmentId(value)).ValueGeneratedNever();
            b.HasIndex(a => a.CohortId);
            b.ComplexProperty(a => a.Title, t =>
            {
                t.Property(x => x.Ar).HasColumnName("TitleAr").HasMaxLength(Assignment.TitleMaxLength);
                t.Property(x => x.En).HasColumnName("TitleEn").HasMaxLength(Assignment.TitleMaxLength);
            });
            b.ComplexProperty(a => a.Instructions, t =>
            {
                t.Property(x => x.Ar).HasColumnName("InstructionsAr").HasMaxLength(Assignment.InstructionsMaxLength);
                t.Property(x => x.En).HasColumnName("InstructionsEn").HasMaxLength(Assignment.InstructionsMaxLength);
            });
        });

        modelBuilder.Entity<AssignmentSubmission>(b =>
        {
            b.ToTable("Submissions");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasConversion(id => id.Value, value => new SubmissionId(value)).ValueGeneratedNever();
            b.Property(s => s.AssignmentId).HasConversion(id => id.Value, value => new AssignmentId(value));
            b.Property(s => s.FileKey).HasMaxLength(64).IsUnicode(false);
            b.Property(s => s.FileName).HasMaxLength(255);
            b.Property(s => s.ContentType).HasMaxLength(120).IsUnicode(false);
            b.Property(s => s.Feedback).HasMaxLength(AssignmentSubmission.FeedbackMaxLength);
            b.Property<byte[]>("RowVersion").IsRowVersion();
            b.HasIndex(s => new { s.AssignmentId, s.TraineeId }).IsUnique();
        });

        modelBuilder.Entity<Gradebook>(b =>
        {
            b.ToTable("Gradebooks");
            b.HasKey(g => g.Id);
            b.Property(g => g.Id).HasConversion(id => id.Value, value => new GradebookId(value)).ValueGeneratedNever();
            b.HasIndex(g => g.CohortId).IsUnique();

            b.OwnsMany(g => g.Items, i =>
            {
                i.ToTable("GradeItems");
                i.WithOwner().HasForeignKey("GradebookId");
                i.HasKey(x => x.Id);
                i.Property(x => x.Id).HasConversion(id => id.Value, value => new GradeItemId(value)).ValueGeneratedNever();
                i.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
                i.Property(x => x.TitleAr).HasMaxLength(Quiz.TitleMaxLength);
                i.Property(x => x.TitleEn).HasMaxLength(Quiz.TitleMaxLength);
                i.Ignore(x => x.Title);
                i.Ignore(x => x.IsPublished);
                i.HasIndex(x => x.SourceId).IsUnique();
            });
            b.Navigation(g => g.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

            b.OwnsMany(g => g.Grades, x =>
            {
                x.ToTable("Grades");
                x.WithOwner().HasForeignKey("GradebookId");
                x.Property(y => y.ItemId).HasConversion(id => id.Value, value => new GradeItemId(value));
                x.Property(y => y.Percent).HasPrecision(5, 2);
                x.HasKey("GradebookId", nameof(Grade.ItemId), nameof(Grade.TraineeId));
            });
            b.Navigation(g => g.Grades).HasField("_grades").UsePropertyAccessMode(PropertyAccessMode.Field);

            b.OwnsMany(g => g.Corrections, c =>
            {
                c.ToTable("GradeCorrections");
                c.WithOwner().HasForeignKey("GradebookId");
                c.HasKey(x => x.Id);
                c.Property(x => x.Id).HasConversion(id => id.Value, value => new CorrectionId(value)).ValueGeneratedNever();
                c.Property(x => x.ItemId).HasConversion(id => id.Value, value => new GradeItemId(value));
                c.Property(x => x.OldPercent).HasPrecision(5, 2);
                c.Property(x => x.NewPercent).HasPrecision(5, 2);
                c.Property(x => x.Reason).HasMaxLength(GradeCorrection.ReasonMaxLength);
                c.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            });
            b.Navigation(g => g.Corrections).HasField("_corrections").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}

internal sealed class QuizRepository(AssessmentDbContext db) : IQuizRepository
{
    private IQueryable<Quiz> Full => db.Quizzes.Include(q => q.Questions).ThenInclude(q => q.Options).AsSplitQuery();

    public Task<Quiz?> FindAsync(QuizId id, CancellationToken cancellationToken) =>
        Full.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Quiz>> ListByCohortAsync(Guid cohortId, CancellationToken cancellationToken) =>
        await Full.AsNoTracking().Where(q => q.CohortId == cohortId).ToListAsync(cancellationToken);

    public void Add(Quiz quiz) => db.Quizzes.Add(quiz);
}

internal sealed class AttemptRepository(AssessmentDbContext db) : IAttemptRepository
{
    public Task<QuizAttempt?> FindAsync(AttemptId id, CancellationToken cancellationToken) =>
        db.Attempts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<QuizAttempt>> ListAsync(QuizId quizId, Guid traineeId, CancellationToken cancellationToken) =>
        await db.Attempts.Where(a => a.QuizId == quizId && a.TraineeId == traineeId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QuizAttempt>> ListAwaitingMarkingAsync(QuizId quizId, CancellationToken cancellationToken) =>
        await db.Attempts.Where(a => a.QuizId == quizId && a.AwaitingMarking).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QuizAttempt>> ListOpenByQuizAsync(QuizId quizId, CancellationToken cancellationToken) =>
        await db.Attempts.Where(a => a.QuizId == quizId && a.SubmittedAt == null).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QuizAttempt>> ListForTraineeAsync(IReadOnlyCollection<QuizId> quizIds, Guid traineeId, CancellationToken cancellationToken) =>
        await db.Attempts.AsNoTracking().Where(a => a.TraineeId == traineeId && quizIds.Contains(a.QuizId)).ToListAsync(cancellationToken);

    public void Add(QuizAttempt attempt) => db.Attempts.Add(attempt);
}

internal sealed class AssignmentRepository(AssessmentDbContext db) : IAssignmentRepository
{
    public Task<Assignment?> FindAsync(AssignmentId id, CancellationToken cancellationToken) =>
        db.Assignments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Assignment>> ListByCohortAsync(Guid cohortId, CancellationToken cancellationToken) =>
        await db.Assignments.AsNoTracking().Where(a => a.CohortId == cohortId).OrderBy(a => a.DueAt).ToListAsync(cancellationToken);

    public Task<AssignmentSubmission?> FindSubmissionAsync(SubmissionId id, CancellationToken cancellationToken) =>
        db.Submissions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<AssignmentSubmission?> FindSubmissionAsync(AssignmentId assignmentId, Guid traineeId, CancellationToken cancellationToken) =>
        db.Submissions.FirstOrDefaultAsync(s => s.AssignmentId == assignmentId && s.TraineeId == traineeId, cancellationToken);

    public async Task<IReadOnlyList<AssignmentSubmission>> ListSubmissionsAsync(AssignmentId assignmentId, CancellationToken cancellationToken) =>
        await db.Submissions.AsNoTracking().Where(s => s.AssignmentId == assignmentId).OrderBy(s => s.SubmittedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AssignmentSubmission>> ListSubmissionsForTraineeAsync(IReadOnlyCollection<AssignmentId> ids, Guid traineeId, CancellationToken cancellationToken) =>
        await db.Submissions.AsNoTracking().Where(s => s.TraineeId == traineeId && ids.Contains(s.AssignmentId)).ToListAsync(cancellationToken);

    public void Add(Assignment assignment) => db.Assignments.Add(assignment);

    public void Add(AssignmentSubmission submission) => db.Submissions.Add(submission);
}

internal sealed class GradebookRepository(AssessmentDbContext db) : IGradebookRepository
{
    public async Task<Gradebook?> FindAsync(Guid cohortId, CancellationToken cancellationToken) =>
        db.Gradebooks.Local.FirstOrDefault(g => g.CohortId == cohortId)
        ?? await db.Gradebooks.AsSplitQuery().FirstOrDefaultAsync(g => g.CohortId == cohortId, cancellationToken);

    public void Add(Gradebook gradebook) => db.Gradebooks.Add(gradebook);
}

internal sealed class QuestionBankRepository(AssessmentDbContext db) : IQuestionBankRepository
{
    public Task<BankQuestion?> FindAsync(BankQuestionId id, CancellationToken cancellationToken) =>
        db.BankQuestions.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    public async Task<PagedResult<BankQuestion>> ListAsync(BankFilter filter, CancellationToken cancellationToken)
    {
        var query = db.BankQuestions.AsNoTracking().Where(q => q.CourseId == filter.CourseId && q.ArchivedAt == null);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(q => q.Text.Ar.Contains(term) || q.Text.En.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            query = query.Where(q => q.Category == filter.Category);
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(q => q.Kind == kind);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(q => q.Category)
            .ThenByDescending(q => q.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<BankQuestion>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(Guid courseId, CancellationToken cancellationToken) =>
        await db.BankQuestions.AsNoTracking()
            .Where(q => q.CourseId == courseId && q.ArchivedAt == null && q.Category != "")
            .Select(q => q.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BankQuestion>> ListLiveAsync(Guid courseId, CancellationToken cancellationToken) =>
        await db.BankQuestions.AsNoTracking().Where(q => q.CourseId == courseId && q.ArchivedAt == null).OrderBy(q => q.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<string>> ListTextsAsync(Guid courseId, CancellationToken cancellationToken) =>
        await db.BankQuestions.AsNoTracking()
            .Where(q => q.CourseId == courseId && q.ArchivedAt == null)
            .Select(q => q.Text.En)
            .ToListAsync(cancellationToken);

    public void Add(BankQuestion question) => db.BankQuestions.Add(question);
}

public static class AssessmentModule
{
    public static IServiceCollection AddAssessmentModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AssessmentDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("Lms"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AssessmentDbContext.SchemaName)));

        services.AddScoped<IAssessmentUnitOfWork>(sp => sp.GetRequiredService<AssessmentDbContext>());
        services.AddScoped<IQuizRepository, QuizRepository>();
        services.AddScoped<IQuestionBankRepository, QuestionBankRepository>();
        services.AddScoped<Lms.Modules.Assessment.Contracts.IQuestionBankTransfer, QuestionBankTransfer>();
        services.AddScoped<BankAccess>();
        services.AddScoped<IAttemptRepository, AttemptRepository>();
        services.AddScoped<IAssignmentRepository, AssignmentRepository>();
        services.AddScoped<IWorkshopRepository, WorkshopRepository>();
        services.AddScoped<ExamGate>();
        services.AddScoped<IGradebookRepository, GradebookRepository>();
        services.AddScoped<AssessmentAccess>();
        services.AddScoped<GradebookAccess>();
        services.AddScoped<AttemptCloser>();
        services.AddScoped<FinalGradePublisher>();
        services.AddHostedService<GradeCorrectedProcessor>();
        services.AddHostedService<GradesReleasedProcessor>();
        services.AddScoped<Lms.BuildingBlocks.Application.IPersonalDataSource, AssessmentPersonalData>();
        services.AddModuleApplication(typeof(IQuizRepository).Assembly);
        return services;
    }
}
