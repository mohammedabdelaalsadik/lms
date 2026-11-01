using Lms.SharedKernel;

namespace Lms.Modules.Learning.Domain;

public enum FormQuestionKind { Single, Multiple, Rating, Text }

public sealed record FormOption(string Ar, string En);

public sealed record FormQuestion(FormQuestionKind Kind, string TextAr, string TextEn, bool IsRequired, IReadOnlyList<FormOption> Options);

/// <summary>Answers refer to questions by position, so a form cannot change once it exists.</summary>
public sealed record FormDefinition(bool IsAnonymous, IReadOnlyList<FormQuestion> Questions)
{
    public const int MaxQuestions = 30;
    public const int MaxOptions = 20;
    public const int TextMaxLength = 500;
    public const int AnswerMaxLength = 2000;
    public const int MaxRating = 5;

    /// <summary>With fewer answers than this, an anonymous result could point at one person.</summary>
    public const int AnonymousMinimum = 3;

    public bool IsValid =>
        Questions is { Count: > 0 and <= MaxQuestions }
        && Questions.All(q => Fits(q.TextAr) && Fits(q.TextEn) && OptionsFit(q));

    /// <summary>Checks a trainee's answers. Answers to blank optional questions are dropped.</summary>
    public IReadOnlyList<FormAnswer>? Accept(IReadOnlyList<FormAnswer>? answers)
    {
        var given = answers ?? [];
        if (given.Select(a => a.Question).Distinct().Count() != given.Count || given.Any(a => a.Question < 0 || a.Question >= Questions.Count))
        {
            return null;
        }

        var kept = new List<FormAnswer>();
        for (var index = 0; index < Questions.Count; index++)
        {
            var question = Questions[index];
            var answer = given.FirstOrDefault(a => a.Question == index);

            if (answer is null || answer.IsBlank)
            {
                if (question.IsRequired)
                {
                    return null;
                }

                continue;
            }

            if (!answer.Fits(question))
            {
                return null;
            }

            kept.Add(answer with { Text = answer.Text?.Trim() });
        }

        return kept;
    }

    private static bool Fits(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= TextMaxLength;

    private static bool OptionsFit(FormQuestion question) =>
        question.Kind is FormQuestionKind.Single or FormQuestionKind.Multiple
            ? question.Options is { Count: >= 2 and <= MaxOptions } && question.Options.All(o => Fits(o.Ar) && Fits(o.En))
            : question.Options is null or { Count: 0 };
}

public sealed record FormAnswer(int Question, IReadOnlyList<int>? Options, int? Rating, string? Text)
{
    public bool IsBlank => Options is null or { Count: 0 } && Rating is null && string.IsNullOrWhiteSpace(Text);

    internal bool Fits(FormQuestion question) => question.Kind switch
    {
        FormQuestionKind.Single => Options is { Count: 1 } && InRange(question) && Rating is null && Text is null,
        FormQuestionKind.Multiple => Options is { Count: > 0 } && InRange(question) && Options.Distinct().Count() == Options.Count && Rating is null && Text is null,
        FormQuestionKind.Rating => Rating is >= 1 and <= FormDefinition.MaxRating && Options is null or { Count: 0 } && Text is null,
        _ => Text is { Length: <= FormDefinition.AnswerMaxLength } && Options is null or { Count: 0 } && Rating is null,
    };

    private bool InRange(FormQuestion question) => Options!.All(o => o >= 0 && o < question.Options.Count);
}

public static class FormErrors
{
    public static readonly Error InvalidForm = Error.Validation("learning.form_invalid", "Each question needs text in both languages, and choices need at least two options.");
    public static readonly Error InvalidAnswers = Error.Validation("learning.form_invalid_answers", "Check your answers. Required questions must be answered.");
    public static readonly Error AlreadySubmitted = Error.Conflict("learning.form_already_submitted", "You have already answered this form.");
    public static readonly Error FinishesOnSubmit = Error.Conflict("learning.form_finishes_on_submit", "This activity finishes when you send your answers.");
}

/// <summary>Records that someone answered. Kept apart from their answers, so an anonymous form cannot be linked back.</summary>
public sealed class FormSubmission
{
    private FormSubmission()
    {
    }

    public Guid Id { get; private set; }
    public ActivityId ActivityId { get; private set; }
    public Guid CohortId { get; private set; }
    public Guid TraineeId { get; private set; }

    public static FormSubmission Create(ActivityId activityId, Guid cohortId, Guid traineeId) => new()
    {
        Id = Guid.CreateVersion7(),
        ActivityId = activityId,
        CohortId = cohortId,
        TraineeId = traineeId,
    };
}

/// <summary>
/// The answers. An anonymous response has no trainee and no time, and its id is random
/// rather than time-ordered, so nothing lines it up with a submission or a completion.
/// </summary>
public sealed class FormResponse
{
    private FormResponse()
    {
    }

    public Guid Id { get; private set; }
    public ActivityId ActivityId { get; private set; }
    public Guid CohortId { get; private set; }
    public Guid? TraineeId { get; private set; }
    public string Answers { get; private set; } = null!;

    public static FormResponse Create(ActivityId activityId, Guid cohortId, Guid? traineeId, string answers) => new()
    {
        Id = Guid.NewGuid(),
        ActivityId = activityId,
        CohortId = cohortId,
        TraineeId = traineeId,
        Answers = answers,
    };
}
