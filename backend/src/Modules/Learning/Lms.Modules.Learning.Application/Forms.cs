using Lms.BuildingBlocks.Application;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Identity.Contracts;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lms.Modules.Learning.Application;

public interface IFormRepository
{
    Task<bool> HasSubmittedAsync(ActivityId id, Guid cohortId, Guid traineeId, CancellationToken cancellationToken);

    Task<IReadOnlySet<ActivityId>> ListSubmittedAsync(Guid cohortId, Guid traineeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FormResponse>> ListResponsesAsync(Guid cohortId, CancellationToken cancellationToken);

    void Add(FormSubmission submission);

    void Add(FormResponse response);
}

public static class FormCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static FormDefinition? ReadForm(string? json) => json is null ? null : JsonSerializer.Deserialize<FormDefinition>(json, Options);

    public static BookDefinition? ReadBook(string? json) => json is null ? null : JsonSerializer.Deserialize<BookDefinition>(json, Options);

    public static LessonDefinition? ReadLesson(string? json) => json is null ? null : JsonSerializer.Deserialize<LessonDefinition>(json, Options);

    /// <summary>The stored form of a book or lesson, or null when its contents break the rules.</summary>
    public static string? WriteStructure(ActivityKind kind, BookDefinition? book, LessonDefinition? lesson) => kind switch
    {
        ActivityKind.Book when book is { IsValid: true } => Write(book),
        ActivityKind.Lesson when lesson is { IsValid: true } => Write(lesson),
        _ => null,
    };

    public static IReadOnlyList<FormAnswer> ReadAnswers(string json) => JsonSerializer.Deserialize<List<FormAnswer>>(json, Options) ?? [];
}

public sealed record SubmitFormCommand(Guid CohortId, Guid ActivityId, IReadOnlyList<FormAnswer> Answers) : ICommand<Unit>;

internal sealed class SubmitFormHandler(
    TraineeGate gate,
    ICurrentUser user,
    ICourseContentRepository contents,
    IFormRepository forms,
    ActivityCompleter completer,
    ILearningUnitOfWork unitOfWork) : IRequestHandler<SubmitFormCommand, Unit>
{
    public async Task<Result<Unit>> Handle(SubmitFormCommand request, CancellationToken cancellationToken)
    {
        var cohort = await gate.EnterAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var id = new ActivityId(request.ActivityId);
        var trainee = user.Id!.Value;
        var content = await contents.FindByCourseAsync(cohort.Value.CourseId, cancellationToken);
        if (content?.FindActivity(id) is not { Kind: ActivityKind.Form } activity)
        {
            return ContentErrors.ActivityNotFound;
        }

        if (await forms.HasSubmittedAsync(id, request.CohortId, trainee, cancellationToken))
        {
            return FormErrors.AlreadySubmitted;
        }

        var form = FormCodec.ReadForm(activity.FormJson)!;
        var accepted = form.Accept(request.Answers);
        if (accepted is null)
        {
            return FormErrors.InvalidAnswers;
        }

        // This also enforces sequential courses.
        var completed = await completer.CompleteAsync(cohort.Value, trainee, id, cancellationToken);
        if (!completed.IsSuccess)
        {
            return completed.Error!;
        }

        forms.Add(FormSubmission.Create(id, request.CohortId, trainee));
        forms.Add(FormResponse.Create(id, request.CohortId, form.IsAnonymous ? null : trainee, FormCodec.Write(accepted)));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record OptionResult(TextDto Text, int Count);

public sealed record TextAnswer(TextDto? Name, string Text);

public sealed record QuestionResult(FormQuestionKind Kind, TextDto Text, int Answered, IReadOnlyList<OptionResult> Options, decimal? Average, IReadOnlyList<TextAnswer> Answers);

public sealed record FormResult(Guid ActivityId, TextDto Title, bool IsAnonymous, int Responses, int Enrolled, bool IsHidden, IReadOnlyList<QuestionResult> Questions);

public static class FormResults
{
    /// <summary>Adds up one form. Text answers keep the stored order, which is random for anonymous forms.</summary>
    public static IReadOnlyList<QuestionResult> Tally(
        FormDefinition form,
        IReadOnlyList<(Guid? TraineeId, IReadOnlyList<FormAnswer> Answers)> responses,
        IReadOnlyDictionary<Guid, TextDto> names)
    {
        return [.. form.Questions.Select((question, index) =>
        {
            var answers = responses
                .SelectMany(r => r.Answers.Where(a => a.Question == index).Select(a => (r.TraineeId, Answer: a)))
                .ToList();

            IReadOnlyList<OptionResult> options = question.Kind switch
            {
                FormQuestionKind.Rating => [.. Enumerable.Range(1, FormDefinition.MaxRating).Select(n =>
                    new OptionResult(new TextDto(n.ToString(), n.ToString()), answers.Count(a => a.Answer.Rating == n)))],
                FormQuestionKind.Text => [],
                _ => [.. question.Options.Select((option, i) =>
                    new OptionResult(new TextDto(option.Ar, option.En), answers.Count(a => a.Answer.Options!.Contains(i))))],
            };

            decimal? average = question.Kind == FormQuestionKind.Rating && answers.Count > 0
                ? Math.Round(answers.Average(a => (decimal)a.Answer.Rating!.Value), 2)
                : null;

            IReadOnlyList<TextAnswer> texts = question.Kind == FormQuestionKind.Text
                ? [.. answers.Select(a => new TextAnswer(a.TraineeId is { } id ? names.GetValueOrDefault(id) : null, a.Answer.Text!))]
                : [];

            return new QuestionResult(question.Kind, new TextDto(question.TextAr, question.TextEn), answers.Count, options, average, texts);
        })];
    }
}

public sealed record ExportFormResultsQuery(Guid CohortId) : IQuery<CsvFile>, IAuditedRequest;

/// <summary>Built from the same results as the page, so hidden anonymous forms stay hidden here too.</summary>
internal sealed class ExportFormResultsHandler(IRequestHandler<ListFormResultsQuery, IReadOnlyList<FormResult>> results) : IRequestHandler<ExportFormResultsQuery, CsvFile>
{
    public async Task<Result<CsvFile>> Handle(ExportFormResultsQuery request, CancellationToken cancellationToken)
    {
        var report = await results.Handle(new ListFormResultsQuery(request.CohortId), cancellationToken);
        if (!report.IsSuccess)
        {
            return report.Error!;
        }

        List<IReadOnlyList<string?>> rows = [["النموذج / Form", "السؤال / Question", "الخيار أو الإجابة / Option or answer", "العدد / Count", "الاسم / Name"]];

        foreach (var form in report.Value.Where(f => !f.IsHidden))
        {
            foreach (var question in form.Questions)
            {
                rows.AddRange(question.Options.Select(o => (IReadOnlyList<string?>)[form.Title.Ar, question.Text.Ar, o.Text.Ar, o.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), null]));
                rows.AddRange(question.Answers.Select(a => (IReadOnlyList<string?>)[form.Title.Ar, question.Text.Ar, a.Text, null, a.Name?.Ar]));
            }
        }

        return CsvWriter.Write($"form-results-{request.CohortId:N}.csv", rows);
    }
}

public sealed record ListFormResultsQuery(Guid CohortId) : IQuery<IReadOnlyList<FormResult>>;

internal sealed class ListFormResultsHandler(
    ICohortDirectory cohorts,
    LearningAccess access,
    ICourseContentRepository contents,
    IFormRepository forms,
    IUserDirectory directory) : IRequestHandler<ListFormResultsQuery, IReadOnlyList<FormResult>>
{
    public async Task<Result<IReadOnlyList<FormResult>>> Handle(ListFormResultsQuery request, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(request.CohortId, cancellationToken);
        if (cohort is null)
        {
            return ProgressErrors.CohortNotFound;
        }

        if (!access.CanRunCohort(cohort))
        {
            return ContentErrors.Forbidden;
        }

        var content = await contents.FindByCourseAsync(cohort.CourseId, cancellationToken);
        var enrolled = (await cohorts.GetEnrolledTraineesAsync(request.CohortId, cancellationToken)).Count;
        var all = await forms.ListResponsesAsync(request.CohortId, cancellationToken);
        var people = await directory.GetAsync([.. all.Where(r => r.TraineeId is not null).Select(r => r.TraineeId!.Value).Distinct()], cancellationToken);
        var names = people.ToDictionary(p => p.Key, p => new TextDto(p.Value.NameAr, p.Value.NameEn));
        var results = new List<FormResult>();

        foreach (var activity in content?.OrderedActivities().Where(a => a.Kind == ActivityKind.Form) ?? [])
        {
            var form = FormCodec.ReadForm(activity.FormJson)!;
            var responses = all.Where(r => r.ActivityId == activity.Id).ToList();
            var hidden = form.IsAnonymous && responses.Count < FormDefinition.AnonymousMinimum;
            List<(Guid?, IReadOnlyList<FormAnswer>)> parsed = hidden ? [] : [.. responses.Select(r => (r.TraineeId, FormCodec.ReadAnswers(r.Answers)))];

            results.Add(new FormResult(
                activity.Id.Value,
                new TextDto(activity.Title.Ar, activity.Title.En),
                form.IsAnonymous,
                responses.Count,
                enrolled,
                hidden,
                hidden ? [] : FormResults.Tally(form, parsed, names)));
        }

        return Result<IReadOnlyList<FormResult>>.Success(results);
    }
}
