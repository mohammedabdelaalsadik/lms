using Lms.SharedKernel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lms.Modules.Assessment.Domain;

public readonly record struct BankQuestionId(Guid Value);

public sealed record OptionDraft(string TextAr, string TextEn, bool IsCorrect, string? MatchAr = null, string? MatchEn = null);

public sealed record QuestionDraft(QuestionKind Kind, string TextAr, string TextEn, int Points, IReadOnlyList<OptionDraft> Options, decimal? Tolerance = null)
{
    public const int MaxOptions = 20;

    /// <summary>Whether this question can be marked automatically, whichever kind it is.</summary>
    public bool IsComplete
    {
        get
        {
            if (Points <= 0 || Options.Count > MaxOptions)
            {
                return false;
            }

            return Kind switch
            {
                QuestionKind.MultipleChoice or QuestionKind.TrueFalse => Options.Count >= 2 && Options.Count(o => o.IsCorrect) == 1,
                QuestionKind.MultipleResponse => Options.Count >= 2 && Options.Any(o => o.IsCorrect),
                QuestionKind.ShortAnswer => Options.Count >= 1,
                QuestionKind.Essay => Options.Count == 0,
                QuestionKind.Numeric => Options.Count == 1 && AnswerText.ParseNumber(Options[0].TextEn) is not null && Tolerance is null or >= 0,
                QuestionKind.Matching => Options.Count >= 2 && Options.All(o => !string.IsNullOrWhiteSpace(o.MatchAr) && !string.IsNullOrWhiteSpace(o.MatchEn)),
                _ => false,
            };
        }
    }
}

/// <summary>How typed answers are compared, so spelling variants do not cost a trainee marks.</summary>
public static class AnswerText
{
    private const string Diacritics = "ـًٌٍَُِّْٰٕٓٔ";

    /// <summary>Ignores case, extra spaces, Arabic diacritics and the usual alef, ya and ta marbuta variants.</summary>
    public static string Normalize(string? value)
    {
        var text = new StringBuilder();
        var pendingSpace = false;

        foreach (var c in (value ?? "").Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                continue;
            }

            if (Diacritics.Contains(c))
            {
                continue;
            }

            if (pendingSpace && text.Length > 0)
            {
                text.Append(' ');
            }

            pendingSpace = false;
            text.Append(c switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                _ => c,
            });
        }

        return text.ToString();
    }

    public static decimal? ParseNumber(string? value)
    {
        var text = Normalize(value).Replace('٫', '.').Replace(" ", "", StringComparison.Ordinal);
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    /// <summary>A label only the server can link to an option, so a matching question cannot be solved by reading the page.</summary>
    public static string SecretKey(Guid seed, Guid optionId) =>
        Convert.ToHexString(SHA256.HashData([.. seed.ToByteArray(), .. optionId.ToByteArray()]))[..16].ToLowerInvariant();
}

/// <summary>A reusable question for one course. Adding it to a quiz copies it, so editing it later cannot change a live exam.</summary>
public sealed class BankQuestion : AggregateRoot<BankQuestionId>
{
    public const int CategoryMaxLength = 100;

    private BankQuestion()
    {
    }

    public Guid CourseId { get; private set; }
    public string Category { get; private set; } = null!;
    public QuestionKind Kind { get; private set; }
    public LocalizedText Text { get; private set; } = null!;
    public int Points { get; private set; }
    public decimal? Tolerance { get; private set; }
    public IReadOnlyList<OptionDraft> Options { get; private set; } = [];
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }

    public static Result<BankQuestion> Create(Guid courseId, string? category, QuestionDraft draft, Guid createdBy, DateTimeOffset now)
    {
        var question = new BankQuestion
        {
            Id = new BankQuestionId(Guid.CreateVersion7()),
            CourseId = courseId,
            CreatedBy = createdBy,
            CreatedAt = now,
        };

        var result = question.Update(category, draft);
        return result.IsSuccess ? question : result.Error!;
    }

    public Result Update(string? category, QuestionDraft draft)
    {
        if (!draft.IsComplete)
        {
            return AssessmentErrors.QuestionIncomplete;
        }

        var text = LocalizedText.Create(draft.TextAr, draft.TextEn, QuizQuestion.TextMaxLength);
        if (!text.IsSuccess)
        {
            return text.Error!;
        }

        Category = string.IsNullOrWhiteSpace(category) ? "" : category.Trim();
        Kind = draft.Kind;
        Text = text.Value;
        Points = draft.Points;
        Tolerance = draft.Tolerance;
        Options = draft.Options;
        return Result.Success();
    }

    public void Archive(DateTimeOffset now) => ArchivedAt ??= now;
}
