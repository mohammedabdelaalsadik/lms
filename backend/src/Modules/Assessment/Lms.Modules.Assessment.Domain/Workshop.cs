using Lms.SharedKernel;

namespace Lms.Modules.Assessment.Domain;

public readonly record struct WorkshopId(Guid Value);

public enum WorkshopStatus { Submission, Review, Closed }

public static class WorkshopErrors
{
    public static readonly Error NotFound = Error.NotFound("assessment.workshop_not_found", "Workshop was not found.");
    public static readonly Error InvalidReviewCount = Error.Validation("assessment.workshop_invalid_reviews", "Each person reviews between 1 and 5 others.");
    public static readonly Error SubmissionClosed = Error.Conflict("assessment.workshop_submission_closed", "Submissions are closed for this workshop.");
    public static readonly Error ReviewClosed = Error.Conflict("assessment.workshop_review_closed", "This workshop is not open for reviews.");
    public static readonly Error TooFewSubmissions = Error.Conflict("assessment.workshop_too_few", "At least two people must submit before reviews can start.");
    public static readonly Error AlreadyClosed = Error.Conflict("assessment.workshop_closed", "This workshop is already closed.");
    public static readonly Error InvalidScore = Error.Validation("assessment.workshop_invalid_score", "The score must be between 0 and 100.");
    public static readonly Error ReviewNotFound = Error.NotFound("assessment.workshop_review_not_found", "This review was not found.");
}

/// <summary>Trainees submit written work, review each other's anonymously, and the average of the peer scores becomes a grade.</summary>
public sealed class Workshop : AggregateRoot<WorkshopId>
{
    public const int TitleMaxLength = 200;
    public const int InstructionsMaxLength = 4000;
    public const int TextMaxLength = 20000;
    public const int CommentMaxLength = 2000;
    public const int MaxReviewsPerPerson = 5;

    private Workshop()
    {
    }

    public Guid CohortId { get; private set; }
    public LocalizedText Title { get; private set; } = null!;
    public LocalizedText Instructions { get; private set; } = null!;
    public WorkshopStatus Status { get; private set; }
    public int ReviewsPerPerson { get; private set; }
    public int Weight { get; private set; }

    public static Result<Workshop> Create(Guid cohortId, LocalizedText title, LocalizedText instructions, int reviewsPerPerson, int weight)
    {
        if (reviewsPerPerson is < 1 or > MaxReviewsPerPerson)
        {
            return WorkshopErrors.InvalidReviewCount;
        }

        if (weight <= 0)
        {
            return AssessmentErrors.InvalidWeight;
        }

        return new Workshop
        {
            Id = new WorkshopId(Guid.CreateVersion7()),
            CohortId = cohortId,
            Title = title,
            Instructions = instructions,
            Status = WorkshopStatus.Submission,
            ReviewsPerPerson = reviewsPerPerson,
            Weight = weight,
        };
    }

    public Result StartReviews(int submissions)
    {
        if (Status != WorkshopStatus.Submission)
        {
            return WorkshopErrors.ReviewClosed;
        }

        if (submissions < 2)
        {
            return WorkshopErrors.TooFewSubmissions;
        }

        Status = WorkshopStatus.Review;
        return Result.Success();
    }

    public Result Close()
    {
        if (Status != WorkshopStatus.Review)
        {
            return WorkshopErrors.AlreadyClosed;
        }

        Status = WorkshopStatus.Closed;
        return Result.Success();
    }

    /// <summary>Unfinished reviews do not count. With no finished review there is no score, and staff grade by hand.</summary>
    public static decimal? Average(IEnumerable<decimal?> scores)
    {
        var done = scores.OfType<decimal>().ToList();
        return done.Count == 0 ? null : Math.Round(done.Average(), 2);
    }
}

public static class ReviewAllocator
{
    /// <summary>
    /// Everyone is placed in a random circle and reviews the next people round it, so nobody gets their
    /// own work, nobody gets the same work twice, and everyone gives and receives the same number of reviews.
    /// </summary>
    public static IReadOnlyList<(Guid Author, Guid Reviewer)> Deal(IReadOnlyList<Guid> authors, int perPerson, Random random)
    {
        var circle = authors.ToArray();
        random.Shuffle(circle);
        var reach = Math.Min(perPerson, circle.Length - 1);
        var pairs = new List<(Guid, Guid)>();

        for (var index = 0; index < circle.Length; index++)
        {
            for (var step = 1; step <= reach; step++)
            {
                pairs.Add((circle[index], circle[(index + step) % circle.Length]));
            }
        }

        return pairs;
    }
}

public sealed class WorkshopSubmission
{
    private WorkshopSubmission()
    {
    }

    public Guid Id { get; private set; }
    public WorkshopId WorkshopId { get; private set; }
    public Guid TraineeId { get; private set; }
    public string Text { get; private set; } = null!;
    public DateTimeOffset SubmittedAt { get; private set; }

    public static WorkshopSubmission Create(WorkshopId workshopId, Guid traineeId, string text, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkshopId = workshopId,
        TraineeId = traineeId,
        Text = text,
        SubmittedAt = now,
    };

    public void Replace(string text, DateTimeOffset now)
    {
        Text = text;
        SubmittedAt = now;
    }
}

public sealed class WorkshopReview
{
    private WorkshopReview()
    {
    }

    public Guid Id { get; private set; }
    public WorkshopId WorkshopId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid ReviewerId { get; private set; }
    public decimal? Score { get; private set; }
    public string? Comment { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }

    public static WorkshopReview Assign(WorkshopId workshopId, Guid submissionId, Guid reviewerId) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkshopId = workshopId,
        SubmissionId = submissionId,
        ReviewerId = reviewerId,
    };

    public Result Record(decimal score, string? comment, DateTimeOffset now)
    {
        if (score is < 0 or > 100)
        {
            return WorkshopErrors.InvalidScore;
        }

        Score = score;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        ReviewedAt = now;
        return Result.Success();
    }
}
