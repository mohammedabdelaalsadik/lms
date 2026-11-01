using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Presentation;
using Lms.Modules.Assessment.Application;
using Lms.Modules.Assessment.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Modules.Assessment.Presentation;

public sealed record QuizRequest(
    string TitleAr,
    string TitleEn,
    int TimeLimitMinutes,
    int MaxAttempts,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    int Weight,
    IReadOnlyList<QuestionDraft> Questions,
    int? DrawCount = null,
    bool Shuffle = false,
    string? ExamKey = null);

public sealed record AssignmentRequest(string TitleAr, string TitleEn, string InstructionsAr, string InstructionsEn, DateTimeOffset DueAt, int Weight);

public sealed record GradeRequest(decimal Percent);

public sealed record WeightRequest(int Weight);

public sealed record ReviewRequest(decimal Percent, string? Feedback);

public sealed record CorrectionRequest(decimal NewPercent, string Reason);

public sealed record MarkRequest(decimal Points);

public sealed record AnswerRequest(Guid QuestionId, string? Response);

public sealed record BankQuestionRequest(string? Category, QuestionDraft Question);

public sealed record WorkshopRequest(string TitleAr, string TitleEn, string InstructionsAr, string InstructionsEn, int ReviewsPerPerson, int Weight);

public sealed record WorkshopTextRequest(string Text);

public sealed record WorkshopReviewRequest(decimal Score, string? Comment);

[Authorize(Roles = LmsRoles.CohortViewers)]
[Route("api/v1/assessment/cohorts/{cohortId:guid}")]
public sealed class AssessmentStaffController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet("workshops")]
    public async Task<IActionResult> Workshops(Guid cohortId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListWorkshopsQuery(cohortId), cancellationToken));

    [HttpPost("workshops")]
    public async Task<IActionResult> CreateWorkshop(Guid cohortId, WorkshopRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new CreateWorkshopCommand(cohortId, body.TitleAr, body.TitleEn, body.InstructionsAr, body.InstructionsEn, body.ReviewsPerPerson, body.Weight), cancellationToken));

    [HttpPost("workshops/{workshopId:guid}/advance")]
    public async Task<IActionResult> AdvanceWorkshop(Guid cohortId, Guid workshopId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new AdvanceWorkshopCommand(cohortId, workshopId), cancellationToken));

    [HttpGet("quizzes")]
    public async Task<IActionResult> Quizzes(Guid cohortId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListQuizzesQuery(cohortId), cancellationToken));

    [HttpPost("quizzes")]
    public async Task<IActionResult> CreateQuiz(Guid cohortId, QuizRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(ToCommand(cohortId, null, body), cancellationToken));

    [HttpPut("quizzes/{quizId:guid}")]
    public async Task<IActionResult> UpdateQuiz(Guid cohortId, Guid quizId, QuizRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(ToCommand(cohortId, quizId, body), cancellationToken));

    [HttpPost("quizzes/{quizId:guid}/publish")]
    public async Task<IActionResult> PublishQuiz(Guid cohortId, Guid quizId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new PublishQuizCommand(cohortId, quizId), cancellationToken));

    [HttpGet("quizzes/{quizId:guid}/essays")]
    public async Task<IActionResult> Essays(Guid cohortId, Guid quizId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListEssaysQuery(cohortId, quizId), cancellationToken));

    [HttpPut("attempts/{attemptId:guid}/essays/{questionId:guid}")]
    public async Task<IActionResult> MarkEssay(Guid cohortId, Guid attemptId, Guid questionId, MarkRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new MarkEssayCommand(cohortId, attemptId, questionId, body.Points), cancellationToken));

    [HttpGet("gradebook/export")]
    public async Task<IActionResult> ExportGradebook(Guid cohortId, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(new ExportGradebookQuery(cohortId), cancellationToken);
        return result.IsSuccess ? File(result.Value.Content, "text/csv; charset=utf-8", result.Value.FileName) : Failure(result.Error!);
    }

    [HttpGet("question-bank")]
    public async Task<IActionResult> Bank(Guid cohortId, [FromQuery] string? search, [FromQuery] string? category, [FromQuery] QuestionKind? kind, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Respond(await Dispatcher.Send(new ListCohortBankQuery(cohortId, search, category, kind, page, pageSize), cancellationToken));

    [HttpGet("assignments")]
    public async Task<IActionResult> Assignments(Guid cohortId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListAssignmentsQuery(cohortId), cancellationToken));

    [HttpPost("assignments")]
    public async Task<IActionResult> CreateAssignment(Guid cohortId, AssignmentRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new CreateAssignmentCommand(cohortId, body.TitleAr, body.TitleEn, body.InstructionsAr, body.InstructionsEn, body.DueAt, body.Weight), cancellationToken));

    [HttpGet("assignments/{assignmentId:guid}/submissions")]
    public async Task<IActionResult> Submissions(Guid cohortId, Guid assignmentId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListSubmissionsQuery(cohortId, assignmentId), cancellationToken));

    [HttpPut("submissions/{submissionId:guid}/review")]
    public async Task<IActionResult> Review(Guid cohortId, Guid submissionId, ReviewRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new ReviewSubmissionCommand(cohortId, submissionId, body.Percent, body.Feedback), cancellationToken));

    [HttpGet("submissions/{submissionId:guid}/file")]
    public async Task<IActionResult> SubmissionFile(Guid cohortId, Guid submissionId, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(new DownloadSubmissionQuery(cohortId, submissionId), cancellationToken);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Failure(result.Error!);
    }

    [HttpGet("gradebook")]
    public async Task<IActionResult> Gradebook(Guid cohortId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetGradebookQuery(cohortId), cancellationToken));

    [HttpPut("gradebook/items/{itemId:guid}/weight")]
    public async Task<IActionResult> Weight(Guid cohortId, Guid itemId, WeightRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new ChangeWeightCommand(cohortId, itemId, body.Weight), cancellationToken));

    [HttpPut("gradebook/items/{itemId:guid}/grades/{traineeId:guid}")]
    public async Task<IActionResult> SetGrade(Guid cohortId, Guid itemId, Guid traineeId, GradeRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new SetGradeCommand(cohortId, itemId, traineeId, body.Percent), cancellationToken));

    [HttpPost("gradebook/items/{itemId:guid}/publish")]
    public async Task<IActionResult> Publish(Guid cohortId, Guid itemId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new PublishGradesCommand(cohortId, itemId), cancellationToken));

    [HttpPost("gradebook/items/{itemId:guid}/grades/{traineeId:guid}/corrections")]
    public async Task<IActionResult> RequestCorrection(Guid cohortId, Guid itemId, Guid traineeId, CorrectionRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new RequestCorrectionCommand(cohortId, itemId, traineeId, body.NewPercent, body.Reason), cancellationToken));

    [HttpPost("gradebook/corrections/{correctionId:guid}/approve")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Approve(Guid cohortId, Guid correctionId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new DecideCorrectionCommand(cohortId, correctionId, true), cancellationToken));

    [HttpPost("gradebook/corrections/{correctionId:guid}/reject")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Reject(Guid cohortId, Guid correctionId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new DecideCorrectionCommand(cohortId, correctionId, false), cancellationToken));

    private static SaveQuizCommand ToCommand(Guid cohortId, Guid? quizId, QuizRequest body) =>
        new(cohortId, quizId, body.TitleAr, body.TitleEn, body.TimeLimitMinutes, body.MaxAttempts, body.OpensAt, body.ClosesAt, body.Weight, body.Questions, body.DrawCount, body.Shuffle, body.ExamKey);
}

[Authorize(Roles = LmsRoles.Trainee)]
[Route("api/v1/assessment/my/timeline")]
public sealed class MyTimelineController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetMyTimelineQuery(), cancellationToken));
}

[Authorize(Roles = LmsRoles.Trainee)]
[Route("api/v1/assessment/my/cohorts/{cohortId:guid}")]
public sealed class MyAssessmentController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    private const long UploadLimit = FileRules.MaxSize + (1024 * 1024);

    [HttpGet]
    public async Task<IActionResult> Overview(Guid cohortId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetMyAssessmentsQuery(cohortId), cancellationToken));

    [HttpGet("workshops")]
    public async Task<IActionResult> MyWorkshops(Guid cohortId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListMyWorkshopsQuery(cohortId), cancellationToken));

    [HttpPut("workshops/{workshopId:guid}/submission")]
    public async Task<IActionResult> SubmitWorkshop(Guid cohortId, Guid workshopId, WorkshopTextRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new SubmitWorkshopCommand(cohortId, workshopId, body.Text), cancellationToken));

    [HttpPut("workshop-reviews/{reviewId:guid}")]
    public async Task<IActionResult> ReviewWorkshop(Guid cohortId, Guid reviewId, WorkshopReviewRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new SubmitWorkshopReviewCommand(cohortId, reviewId, body.Score, body.Comment), cancellationToken));

    [HttpPost("quizzes/{quizId:guid}/attempts")]
    public async Task<IActionResult> Start(Guid cohortId, Guid quizId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new StartAttemptCommand(cohortId, quizId), cancellationToken));

    [HttpPut("attempts/{attemptId:guid}/answers")]
    public async Task<IActionResult> Answer(Guid cohortId, Guid attemptId, AnswerRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new SaveAnswerCommand(cohortId, attemptId, body.QuestionId, body.Response), cancellationToken));

    [HttpPost("attempts/{attemptId:guid}/submit")]
    public async Task<IActionResult> Submit(Guid cohortId, Guid attemptId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new SubmitAttemptCommand(cohortId, attemptId), cancellationToken));

    [HttpPost("assignments/{assignmentId:guid}/submission")]
    [RequestSizeLimit(UploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public async Task<IActionResult> SubmitAssignment(Guid cohortId, Guid assignmentId, IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return RespondNoContent(await Dispatcher.Send(new SubmitAssignmentCommand(cohortId, assignmentId, file.FileName, file.Length, stream), cancellationToken));
    }
}

[Authorize(Roles = LmsRoles.CohortViewers)]
[Route("api/v1/question-bank/{courseId:guid}")]
public sealed class QuestionBankController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    private const long MaxImportBytes = 10 * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> List(Guid courseId, [FromQuery] string? search, [FromQuery] string? category, [FromQuery] QuestionKind? kind, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Respond(await Dispatcher.Send(new ListBankQuestionsQuery(courseId, search, category, kind, page, pageSize), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(Guid courseId, BankQuestionRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new SaveBankQuestionCommand(courseId, null, body.Category, body.Question), cancellationToken));

    [HttpPut("{questionId:guid}")]
    public async Task<IActionResult> Update(Guid courseId, Guid questionId, BankQuestionRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new SaveBankQuestionCommand(courseId, questionId, body.Category, body.Question), cancellationToken));

    [HttpDelete("{questionId:guid}")]
    public async Task<IActionResult> Archive(Guid courseId, Guid questionId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new ArchiveBankQuestionCommand(courseId, questionId), cancellationToken));

    [HttpPost("import")]
    [RequestSizeLimit(MaxImportBytes)]
    public async Task<IActionResult> Import(Guid courseId, IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        return Respond(await Dispatcher.Send(new ImportBankQuestionsCommand(courseId, content), cancellationToken));
    }
}
