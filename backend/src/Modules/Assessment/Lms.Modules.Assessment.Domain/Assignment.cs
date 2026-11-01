using Lms.SharedKernel;

namespace Lms.Modules.Assessment.Domain;

public readonly record struct AssignmentId(Guid Value);

public readonly record struct SubmissionId(Guid Value);

public sealed record SubmittedFile(string StorageKey, string FileName, string ContentType, long Size);

public sealed class Assignment : AggregateRoot<AssignmentId>
{
    public const int TitleMaxLength = 200;
    public const int InstructionsMaxLength = 4000;

    private Assignment()
    {
    }

    public Guid CohortId { get; private set; }
    public LocalizedText Title { get; private set; } = null!;
    public LocalizedText Instructions { get; private set; } = null!;
    public DateTimeOffset DueAt { get; private set; }
    public int Weight { get; private set; }

    public static Result<Assignment> Create(Guid cohortId, LocalizedText title, LocalizedText instructions, DateTimeOffset dueAt, int weight)
    {
        if (weight <= 0)
        {
            return AssessmentErrors.InvalidWeight;
        }

        return new Assignment
        {
            Id = new AssignmentId(Guid.CreateVersion7()),
            CohortId = cohortId,
            Title = title,
            Instructions = instructions,
            DueAt = dueAt,
            Weight = weight,
        };
    }
}

/// <summary>A trainee's work for an assignment. A new upload replaces the old one until it is reviewed.</summary>
public sealed class AssignmentSubmission : AggregateRoot<SubmissionId>
{
    public const int FeedbackMaxLength = 2000;

    private AssignmentSubmission()
    {
    }

    public AssignmentId AssignmentId { get; private set; }
    public Guid TraineeId { get; private set; }
    public string FileKey { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long FileSize { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public bool IsLate { get; private set; }
    public string? Feedback { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }

    public static AssignmentSubmission Submit(Assignment assignment, Guid traineeId, SubmittedFile file, DateTimeOffset now)
    {
        var submission = new AssignmentSubmission
        {
            Id = new SubmissionId(Guid.CreateVersion7()),
            AssignmentId = assignment.Id,
            TraineeId = traineeId,
        };

        submission.Apply(assignment, file, now);
        return submission;
    }

    /// <summary>Returns the replaced file so the caller can delete it from storage.</summary>
    public Result<string> Replace(Assignment assignment, SubmittedFile file, DateTimeOffset now)
    {
        if (ReviewedAt is not null)
        {
            return AssessmentErrors.SubmissionClosed;
        }

        var old = FileKey;
        Apply(assignment, file, now);
        return old;
    }

    public void Review(string? feedback, DateTimeOffset now)
    {
        Feedback = string.IsNullOrWhiteSpace(feedback) ? null : feedback.Trim();
        ReviewedAt = now;
    }

    private void Apply(Assignment assignment, SubmittedFile file, DateTimeOffset now)
    {
        FileKey = file.StorageKey;
        FileName = file.FileName;
        ContentType = file.ContentType;
        FileSize = file.Size;
        SubmittedAt = now;
        IsLate = now > assignment.DueAt;
    }
}
