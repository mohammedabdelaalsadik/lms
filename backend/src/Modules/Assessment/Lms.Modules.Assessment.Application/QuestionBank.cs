using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Assessment.Domain;
using Lms.Modules.Enrollment.Contracts;
using Lms.SharedKernel;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Lms.Modules.Assessment.Application;

public sealed record BankFilter(Guid CourseId, string? Search, string? Category, QuestionKind? Kind, int Page, int PageSize);

public interface IQuestionBankRepository
{
    Task<BankQuestion?> FindAsync(BankQuestionId id, CancellationToken cancellationToken);

    Task<PagedResult<BankQuestion>> ListAsync(BankFilter filter, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListCategoriesAsync(Guid courseId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> ListTextsAsync(Guid courseId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BankQuestion>> ListLiveAsync(Guid courseId, CancellationToken cancellationToken);

    void Add(BankQuestion question);
}

/// <summary>Admins and coordinators manage every bank. Instructors manage the banks of courses they teach.</summary>
public sealed class BankAccess(ICohortDirectory cohorts, ICurrentUser user)
{
    public async Task<Result> ManageAsync(Guid courseId, CancellationToken cancellationToken)
    {
        if (user.Role is LmsRoles.Admin or LmsRoles.Coordinator)
        {
            return Result.Success();
        }

        var teaches = user.Id is { } id && await cohorts.TeachesCourseAsync(id, courseId, cancellationToken);
        return teaches ? Result.Success() : AssessmentErrors.Forbidden;
    }
}

public sealed record BankQuestionResponse(Guid Id, string Category, QuestionKind Kind, string TextAr, string TextEn, int Points, decimal? Tolerance, IReadOnlyList<OptionDraft> Options);

public sealed record BankListResponse(Guid CourseId, IReadOnlyList<string> Categories, PagedResult<BankQuestionResponse> Questions);

internal static class BankMapping
{
    public static BankQuestionResponse ToResponse(this BankQuestion q) =>
        new(q.Id.Value, q.Category, q.Kind, q.Text.Ar, q.Text.En, q.Points, q.Tolerance, q.Options);

    public static async Task<BankListResponse> ListWithCategoriesAsync(this IQuestionBankRepository bank, BankFilter filter, CancellationToken cancellationToken)
    {
        var page = await bank.ListAsync(filter, cancellationToken);
        var categories = await bank.ListCategoriesAsync(filter.CourseId, cancellationToken);

        return new BankListResponse(
            filter.CourseId,
            categories,
            new PagedResult<BankQuestionResponse>([.. page.Items.Select(q => q.ToResponse())], page.Total, page.Page, page.PageSize));
    }
}

public sealed record ListBankQuestionsQuery(Guid CourseId, string? Search, string? Category, QuestionKind? Kind, int Page = 1, int PageSize = 20) : IQuery<BankListResponse>;

internal sealed class ListBankQuestionsValidator : AbstractValidator<ListBankQuestionsQuery>
{
    public ListBankQuestionsValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.Search).MaximumLength(100);
    }
}

internal sealed class ListBankQuestionsHandler(BankAccess access, IQuestionBankRepository bank) : IRequestHandler<ListBankQuestionsQuery, BankListResponse>
{
    public async Task<Result<BankListResponse>> Handle(ListBankQuestionsQuery request, CancellationToken cancellationToken)
    {
        var allowed = await access.ManageAsync(request.CourseId, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error!;
        }

        return await bank.ListWithCategoriesAsync(new BankFilter(request.CourseId, request.Search, request.Category, request.Kind, request.Page, request.PageSize), cancellationToken);
    }
}

/// <summary>What the quiz editor shows: the bank of the course this cohort belongs to.</summary>
public sealed record ListCohortBankQuery(Guid CohortId, string? Search, string? Category, QuestionKind? Kind, int Page = 1, int PageSize = 50) : IQuery<BankListResponse>;

internal sealed class ListCohortBankValidator : AbstractValidator<ListCohortBankQuery>
{
    public ListCohortBankValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.Search).MaximumLength(100);
    }
}

internal sealed class ListCohortBankHandler(AssessmentAccess access, IQuestionBankRepository bank) : IRequestHandler<ListCohortBankQuery, BankListResponse>
{
    public async Task<Result<BankListResponse>> Handle(ListCohortBankQuery request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        return await bank.ListWithCategoriesAsync(new BankFilter(cohort.Value.CourseId, request.Search, request.Category, request.Kind, request.Page, request.PageSize), cancellationToken);
    }
}

public sealed record SaveBankQuestionCommand(Guid CourseId, Guid? QuestionId, string? Category, QuestionDraft Question) : ICommand<BankQuestionResponse>
{
    public override string ToString() => nameof(SaveBankQuestionCommand);
}

internal sealed class SaveBankQuestionValidator : AbstractValidator<SaveBankQuestionCommand>
{
    public SaveBankQuestionValidator()
    {
        RuleFor(c => c.Category).MaximumLength(BankQuestion.CategoryMaxLength);
        RuleFor(c => c.Question).NotNull();
    }
}

internal sealed class SaveBankQuestionHandler(BankAccess access, IQuestionBankRepository bank, ICurrentUser user, IAssessmentUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<SaveBankQuestionCommand, BankQuestionResponse>
{
    public async Task<Result<BankQuestionResponse>> Handle(SaveBankQuestionCommand request, CancellationToken cancellationToken)
    {
        var allowed = await access.ManageAsync(request.CourseId, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error!;
        }

        BankQuestion question;

        if (request.QuestionId is { } id)
        {
            var existing = await bank.FindAsync(new BankQuestionId(id), cancellationToken);
            if (existing is null || existing.CourseId != request.CourseId || existing.ArchivedAt is not null)
            {
                return AssessmentErrors.BankQuestionNotFound;
            }

            var updated = existing.Update(request.Category, request.Question);
            if (!updated.IsSuccess)
            {
                return updated.Error!;
            }

            question = existing;
        }
        else
        {
            var created = BankQuestion.Create(request.CourseId, request.Category, request.Question, user.Id!.Value, clock.GetUtcNow());
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            question = created.Value;
            bank.Add(question);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return question.ToResponse();
    }
}

public sealed record ArchiveBankQuestionCommand(Guid CourseId, Guid QuestionId) : ICommand<Unit>;

internal sealed class ArchiveBankQuestionHandler(BankAccess access, IQuestionBankRepository bank, IAssessmentUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<ArchiveBankQuestionCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ArchiveBankQuestionCommand request, CancellationToken cancellationToken)
    {
        var allowed = await access.ManageAsync(request.CourseId, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error!;
        }

        var question = await bank.FindAsync(new BankQuestionId(request.QuestionId), cancellationToken);
        if (question is null || question.CourseId != request.CourseId)
        {
            return AssessmentErrors.BankQuestionNotFound;
        }

        question.Archive(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record ImportedQuestion(string Category, QuestionDraft Draft);

public sealed record MoodleImport(IReadOnlyList<ImportedQuestion> Questions, int Unsupported);

/// <summary>Reads Moodle's question XML export. Moodle holds one language, so the text fills both language fields.</summary>
public static partial class MoodleQuestionXml
{
    private const int MaxCharacters = 20_000_000;

    public static MoodleImport? Read(Stream content)
    {
        // Outside definitions stay off, so a crafted file cannot read server files or exhaust memory.
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxCharacters,
        };

        XDocument document;

        try
        {
            using var reader = XmlReader.Create(content, settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        if (document.Root?.Name != "quiz")
        {
            return null;
        }

        var category = "";
        var unsupported = 0;
        var questions = new List<ImportedQuestion>();

        foreach (var element in document.Root.Elements("question"))
        {
            var type = element.Attribute("type")?.Value;

            if (type == "category")
            {
                category = Text(element.Element("category")).Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                category = category.StartsWith('$') ? "" : category.Trim();
                continue;
            }

            var draft = Convert(element, type);

            if (draft is { IsComplete: true })
            {
                questions.Add(new ImportedQuestion(category.Length > BankQuestion.CategoryMaxLength ? category[..BankQuestion.CategoryMaxLength] : category, draft));
            }
            else
            {
                unsupported++;
            }
        }

        return new MoodleImport(questions, unsupported);
    }

    private static QuestionDraft? Convert(XElement question, string? type)
    {
        var raw = question.Element("questiontext")?.Element("text")?.Value ?? "";

        // A picture cannot come across on its own, and the question would make no sense without it.
        if (raw.Contains("<img", StringComparison.OrdinalIgnoreCase) || question.Element("questiontext")?.Elements("file").Any() == true)
        {
            return null;
        }

        var text = Plain(raw);
        if (text.Length is 0 or > QuizQuestion.TextMaxLength)
        {
            return null;
        }

        var grade = decimal.TryParse(question.Element("defaultgrade")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 1;
        var points = Math.Max(1, (int)Math.Round(grade));
        var answers = question.Elements("answer").ToList();

        static OptionDraft Option(string text, bool correct) => new(text, text, correct);

        switch (type)
        {
            case "multichoice":
                var single = !string.Equals(question.Element("single")?.Value, "false", StringComparison.OrdinalIgnoreCase);
                var kind = single ? QuestionKind.MultipleChoice : QuestionKind.MultipleResponse;
                return new QuestionDraft(kind, text, text, points, [.. answers.Select(a => Option(Text(a), single ? Fraction(a) >= 100 : Fraction(a) > 0))]);

            case "truefalse":
                var isTrue = answers.Exists(a => Fraction(a) >= 100 && Text(a).Equals("true", StringComparison.OrdinalIgnoreCase));
                return new QuestionDraft(QuestionKind.TrueFalse, text, text, points, [new OptionDraft("صحيح", "True", isTrue), new OptionDraft("خطأ", "False", !isTrue)]);

            case "shortanswer":
                return new QuestionDraft(QuestionKind.ShortAnswer, text, text, points, [.. answers.Where(a => Fraction(a) >= 100).Select(a => Option(Text(a), true))]);

            case "numerical":
                var right = answers.Find(a => Fraction(a) >= 100);
                if (right is null)
                {
                    return null;
                }

                var tolerance = decimal.TryParse(right.Element("tolerance")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var range) ? range : 0;
                return new QuestionDraft(QuestionKind.Numeric, text, text, points, [Option(Text(right), true)], tolerance);

            case "essay":
                return new QuestionDraft(QuestionKind.Essay, text, text, points, []);

            case "matching":
                return new QuestionDraft(
                    QuestionKind.Matching,
                    text,
                    text,
                    points,
                    [.. question.Elements("subquestion")
                        .Where(s => Text(s).Length > 0)
                        .Select(s => (Left: Text(s), Right: Text(s.Element("answer"))))
                        .Select(p => new OptionDraft(p.Left, p.Left, true, p.Right, p.Right))]);

            default:
                return null;
        }
    }

    private static decimal Fraction(XElement answer) =>
        decimal.TryParse(answer.Attribute("fraction")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string Text(XElement? element) => Plain(element?.Element("text")?.Value ?? "");

    private static string Plain(string html) => Spaces().Replace(WebUtility.HtmlDecode(Tags().Replace(html, " ")), " ").Trim();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

public sealed record ImportResult(int Imported, int Duplicates, int Unsupported);

public sealed record ImportBankQuestionsCommand(Guid CourseId, Stream Content) : ICommand<ImportResult>
{
    public override string ToString() => nameof(ImportBankQuestionsCommand);
}

internal sealed class ImportBankQuestionsHandler(BankAccess access, IQuestionBankRepository bank, ICurrentUser user, IAssessmentUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<ImportBankQuestionsCommand, ImportResult>
{
    public async Task<Result<ImportResult>> Handle(ImportBankQuestionsCommand request, CancellationToken cancellationToken)
    {
        var allowed = await access.ManageAsync(request.CourseId, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error!;
        }

        var parsed = MoodleQuestionXml.Read(request.Content);
        if (parsed is null)
        {
            return AssessmentErrors.ImportInvalid;
        }

        // Importing the same file twice adds nothing.
        var known = (await bank.ListTextsAsync(request.CourseId, cancellationToken)).Select(AnswerText.Normalize).ToHashSet();
        var now = clock.GetUtcNow();
        var imported = 0;
        var duplicates = 0;
        var unsupported = parsed.Unsupported;

        foreach (var item in parsed.Questions)
        {
            if (!known.Add(AnswerText.Normalize(item.Draft.TextEn)))
            {
                duplicates++;
                continue;
            }

            var question = BankQuestion.Create(request.CourseId, item.Category, item.Draft, user.Id!.Value, now);
            if (!question.IsSuccess)
            {
                unsupported++;
                continue;
            }

            bank.Add(question.Value);
            imported++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ImportResult(imported, duplicates, unsupported);
    }
}
