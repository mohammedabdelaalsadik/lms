using Lms.SharedKernel;

namespace Lms.Modules.Assessment.Domain;

public readonly record struct GradebookId(Guid Value);

public readonly record struct GradeItemId(Guid Value);

public readonly record struct CorrectionId(Guid Value);

public enum GradeItemKind { Quiz, Assignment, Workshop }

public enum CorrectionStatus { Pending, Approved, Rejected }

public sealed record GradesReleased(Guid CohortId, Guid GradeItemId, string TitleAr, string TitleEn) : IDomainEvent;

public sealed record GradeCorrected(
    Guid CohortId,
    Guid GradeItemId,
    Guid TraineeId,
    decimal OldPercent,
    decimal NewPercent,
    Guid RequestedBy,
    Guid ApprovedBy,
    string Reason) : IDomainEvent;

public sealed class GradeItem : Entity<GradeItemId>
{
    private GradeItem()
    {
    }

    public Guid SourceId { get; private set; }
    public GradeItemKind Kind { get; private set; }
    public string TitleAr { get; private set; } = null!;
    public string TitleEn { get; private set; } = null!;
    public int Weight { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public LocalizedText Title => new(TitleAr, TitleEn);

    public bool IsPublished => PublishedAt is not null;

    internal static GradeItem Create(Guid sourceId, GradeItemKind kind, LocalizedText title, int weight) => new()
    {
        Id = new GradeItemId(Guid.CreateVersion7()),
        SourceId = sourceId,
        Kind = kind,
        TitleAr = title.Ar,
        TitleEn = title.En,
        Weight = weight,
    };

    internal void ChangeWeight(int weight) => Weight = weight;

    internal void Publish(DateTimeOffset now) => PublishedAt = now;
}

public sealed class Grade
{
    private Grade()
    {
    }

    public GradeItemId ItemId { get; private set; }
    public Guid TraineeId { get; private set; }
    public decimal Percent { get; private set; }

    internal static Grade Create(GradeItemId itemId, Guid traineeId, decimal percent) => new()
    {
        ItemId = itemId,
        TraineeId = traineeId,
        Percent = percent,
    };

    internal void Change(decimal percent) => Percent = percent;
}

public sealed class GradeCorrection : Entity<CorrectionId>
{
    public const int ReasonMaxLength = 500;

    private GradeCorrection()
    {
    }

    public GradeItemId ItemId { get; private set; }
    public Guid TraineeId { get; private set; }
    public decimal OldPercent { get; private set; }
    public decimal NewPercent { get; private set; }
    public string Reason { get; private set; } = null!;
    public Guid RequestedBy { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public CorrectionStatus Status { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    internal static GradeCorrection Create(GradeItemId itemId, Guid traineeId, decimal oldPercent, decimal newPercent, string reason, Guid requestedBy, DateTimeOffset now) => new()
    {
        Id = new CorrectionId(Guid.CreateVersion7()),
        ItemId = itemId,
        TraineeId = traineeId,
        OldPercent = oldPercent,
        NewPercent = newPercent,
        Reason = reason,
        RequestedBy = requestedBy,
        RequestedAt = now,
        Status = CorrectionStatus.Pending,
    };

    internal void Decide(CorrectionStatus status, Guid decidedBy, DateTimeOffset now)
    {
        Status = status;
        DecidedBy = decidedBy;
        DecidedAt = now;
    }
}

/// <summary>
/// All grades of one cohort. A grade stays private until its item is published, and a published
/// grade changes only through a correction approved by a second person. A grade that was missing
/// when its item was published can still be entered.
/// </summary>
public sealed class Gradebook : AggregateRoot<GradebookId>
{
    private readonly List<GradeItem> _items = [];
    private readonly List<Grade> _grades = [];
    private readonly List<GradeCorrection> _corrections = [];

    private Gradebook()
    {
    }

    public Guid CohortId { get; private set; }

    public IReadOnlyCollection<GradeItem> Items => _items;

    public IReadOnlyCollection<Grade> Grades => _grades;

    public IReadOnlyCollection<GradeCorrection> Corrections => _corrections;

    public static Gradebook Create(Guid cohortId) => new()
    {
        Id = new GradebookId(Guid.CreateVersion7()),
        CohortId = cohortId,
    };

    public GradeItem AddItem(Guid sourceId, GradeItemKind kind, LocalizedText title, int weight)
    {
        var item = GradeItem.Create(sourceId, kind, title, weight);
        _items.Add(item);
        return item;
    }

    public GradeItem? ItemFor(Guid sourceId) => _items.Find(i => i.SourceId == sourceId);

    public decimal? GradeOf(GradeItemId itemId, Guid traineeId) =>
        _grades.Find(g => g.ItemId == itemId && g.TraineeId == traineeId)?.Percent;

    public Result ChangeWeight(GradeItemId itemId, int weight)
    {
        var item = _items.Find(i => i.Id == itemId);
        if (item is null)
        {
            return AssessmentErrors.ItemNotFound;
        }

        if (item.IsPublished)
        {
            return AssessmentErrors.GradesPublished;
        }

        if (weight <= 0)
        {
            return AssessmentErrors.InvalidWeight;
        }

        item.ChangeWeight(weight);
        return Result.Success();
    }

    public Result SetGrade(GradeItemId itemId, Guid traineeId, decimal percent)
    {
        var item = _items.Find(i => i.Id == itemId);
        if (item is null)
        {
            return AssessmentErrors.ItemNotFound;
        }

        // A missing grade may still be filled in, or a trainee who skipped the item could never finish.
        if (item.IsPublished && GradeOf(itemId, traineeId) is not null)
        {
            return AssessmentErrors.GradesPublished;
        }

        if (percent is < 0 or > 100)
        {
            return AssessmentErrors.InvalidGrade;
        }

        Put(itemId, traineeId, percent);
        return Result.Success();
    }

    /// <summary>The best attempt counts, so a weaker retake never lowers a grade.</summary>
    public Result RecordQuizScore(GradeItemId itemId, Guid traineeId, decimal percent)
    {
        var current = GradeOf(itemId, traineeId);
        return current >= percent ? Result.Success() : SetGrade(itemId, traineeId, percent);
    }

    public Result Publish(GradeItemId itemId, DateTimeOffset now)
    {
        var item = _items.Find(i => i.Id == itemId);
        if (item is null)
        {
            return AssessmentErrors.ItemNotFound;
        }

        if (item.IsPublished)
        {
            return AssessmentErrors.GradesPublished;
        }

        item.Publish(now);
        Raise(new GradesReleased(CohortId, itemId.Value, item.TitleAr, item.TitleEn));
        return Result.Success();
    }

    public Result RequestCorrection(GradeItemId itemId, Guid traineeId, decimal newPercent, string? reason, Guid requestedBy, DateTimeOffset now)
    {
        var item = _items.Find(i => i.Id == itemId);
        if (item is null)
        {
            return AssessmentErrors.ItemNotFound;
        }

        if (!item.IsPublished)
        {
            return AssessmentErrors.GradesNotPublished;
        }

        if (newPercent is < 0 or > 100)
        {
            return AssessmentErrors.InvalidGrade;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return AssessmentErrors.ReasonRequired;
        }

        if (GradeOf(itemId, traineeId) is not { } current)
        {
            return AssessmentErrors.NoGradeToCorrect;
        }

        if (_corrections.Exists(c => c.ItemId == itemId && c.TraineeId == traineeId && c.Status == CorrectionStatus.Pending))
        {
            return AssessmentErrors.CorrectionPending;
        }

        _corrections.Add(GradeCorrection.Create(itemId, traineeId, current, newPercent, reason.Trim(), requestedBy, now));
        return Result.Success();
    }

    public Result<GradeCorrection> ApproveCorrection(CorrectionId id, Guid decidedBy, DateTimeOffset now)
    {
        var correction = PendingCorrection(id);
        if (!correction.IsSuccess)
        {
            return correction;
        }

        var request = correction.Value;
        if (request.RequestedBy == decidedBy)
        {
            return AssessmentErrors.CannotApproveOwn;
        }

        request.Decide(CorrectionStatus.Approved, decidedBy, now);
        Put(request.ItemId, request.TraineeId, request.NewPercent);
        Raise(new GradeCorrected(CohortId, request.ItemId.Value, request.TraineeId, request.OldPercent, request.NewPercent, request.RequestedBy, decidedBy, request.Reason));
        return request;
    }

    public Result RejectCorrection(CorrectionId id, Guid decidedBy, DateTimeOffset now)
    {
        var correction = PendingCorrection(id);
        if (!correction.IsSuccess)
        {
            return correction.Error!;
        }

        correction.Value.Decide(CorrectionStatus.Rejected, decidedBy, now);
        return Result.Success();
    }

    /// <summary>Weighted total of published grades, or null when nothing is published for the trainee.</summary>
    public decimal? RunningTotal(Guid traineeId)
    {
        var graded = _items.Where(i => i.IsPublished && GradeOf(i.Id, traineeId) is not null).ToList();
        return graded.Count == 0 ? null : Weighted(graded, traineeId);
    }

    /// <summary>The final grade exists only once every item is published and graded for the trainee.</summary>
    public decimal? FinalGrade(Guid traineeId)
    {
        var complete = _items.Count > 0 && _items.TrueForAll(i => i.IsPublished && GradeOf(i.Id, traineeId) is not null);
        return complete ? Weighted(_items, traineeId) : null;
    }

    private Result<GradeCorrection> PendingCorrection(CorrectionId id)
    {
        var correction = _corrections.Find(c => c.Id == id);

        if (correction is null)
        {
            return AssessmentErrors.CorrectionNotFound;
        }

        return correction.Status == CorrectionStatus.Pending ? correction : AssessmentErrors.CorrectionDecided;
    }

    private decimal Weighted(IReadOnlyCollection<GradeItem> items, Guid traineeId)
    {
        var weights = items.Sum(i => i.Weight);
        var total = items.Sum(i => i.Weight * GradeOf(i.Id, traineeId)!.Value);
        return Math.Round(total / weights, 2);
    }

    private void Put(GradeItemId itemId, Guid traineeId, decimal percent)
    {
        var grade = _grades.Find(g => g.ItemId == itemId && g.TraineeId == traineeId);

        if (grade is null)
        {
            _grades.Add(Grade.Create(itemId, traineeId, percent));
        }
        else
        {
            grade.Change(percent);
        }
    }
}
