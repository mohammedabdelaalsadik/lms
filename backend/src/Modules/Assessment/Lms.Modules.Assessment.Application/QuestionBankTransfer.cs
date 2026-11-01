using Lms.Modules.Assessment.Contracts;
using Lms.Modules.Assessment.Domain;

namespace Lms.Modules.Assessment.Application;

public sealed class QuestionBankTransfer(IQuestionBankRepository bank, IAssessmentUnitOfWork unitOfWork, TimeProvider clock) : IQuestionBankTransfer
{
    public async Task<IReadOnlyList<BankEntry>> ExportAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var questions = await bank.ListLiveAsync(courseId, cancellationToken);

        return
        [
            .. questions.Select(q => new BankEntry(
                q.Category,
                q.Kind.ToString(),
                q.Text.Ar,
                q.Text.En,
                q.Points,
                q.Tolerance,
                [.. q.Options.Select(o => new BankOption(o.TextAr, o.TextEn, o.IsCorrect, o.MatchAr, o.MatchEn))])),
        ];
    }

    public async Task<int> ImportAsync(Guid courseId, IReadOnlyList<BankEntry> entries, Guid createdBy, CancellationToken cancellationToken)
    {
        var added = 0;

        foreach (var entry in entries)
        {
            if (!Enum.TryParse<QuestionKind>(entry.Kind, out var kind) || entry.Options is null)
            {
                continue;
            }

            var draft = new QuestionDraft(kind, entry.TextAr, entry.TextEn, entry.Points, [.. entry.Options.Select(o => new OptionDraft(o.TextAr, o.TextEn, o.IsCorrect, o.MatchAr, o.MatchEn))], entry.Tolerance);
            var question = BankQuestion.Create(courseId, entry.Category, draft, createdBy, clock.GetUtcNow());

            if (question.IsSuccess)
            {
                bank.Add(question.Value);
                added++;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return added;
    }
}
