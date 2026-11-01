using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;

namespace Lms.Modules.Learning.Application;

public interface IGlossaryRepository
{
    Task<GlossaryEntry?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<GlossaryEntry>> ListAsync(Guid courseId, CancellationToken cancellationToken);

    void Add(GlossaryEntry entry);

    void Remove(GlossaryEntry entry);
}

/// <summary>Staff who can edit the course manage its glossary. Its trainees read it and may suggest terms.</summary>
public sealed class GlossaryGate(LearningAccess access, ICohortDirectory cohorts, ICurrentUser user)
{
    public async Task<Result<bool>> EnterAsync(Guid courseId, CancellationToken cancellationToken)
    {
        if (await access.CanEditCourseAsync(courseId, cancellationToken))
        {
            return true;
        }

        var enrolled = user.Id is { } id && (await cohorts.GetEnrolledCohortsAsync(id, cancellationToken)).Any(c => c.CourseId == courseId);
        return enrolled ? false : ContentErrors.Forbidden;
    }
}

public sealed record GlossaryItem(Guid Id, TextDto Term, TextDto Definition, bool IsPending, bool IsMine);

public sealed record GlossaryResponse(bool CanManage, IReadOnlyList<GlossaryItem> Entries);

public sealed record GetGlossaryQuery(Guid CourseId) : IQuery<GlossaryResponse>;

internal sealed class GetGlossaryHandler(GlossaryGate gate, IGlossaryRepository glossary, ICurrentUser user) : IRequestHandler<GetGlossaryQuery, GlossaryResponse>
{
    public async Task<Result<GlossaryResponse>> Handle(GetGlossaryQuery request, CancellationToken cancellationToken)
    {
        var manage = await gate.EnterAsync(request.CourseId, cancellationToken);
        if (!manage.IsSuccess)
        {
            return manage.Error!;
        }

        var entries = await glossary.ListAsync(request.CourseId, cancellationToken);
        return new GlossaryResponse(manage.Value,
        [
            .. entries
                .Where(e => manage.Value || e.IsApproved || e.AuthorId == user.Id)
                .Select(e => new GlossaryItem(e.Id, new TextDto(e.Term.Ar, e.Term.En), new TextDto(e.Definition.Ar, e.Definition.En), !e.IsApproved, e.AuthorId == user.Id)),
        ]);
    }
}

public sealed record SaveGlossaryEntryCommand(Guid CourseId, Guid? Id, string TermAr, string TermEn, string DefinitionAr, string DefinitionEn) : ICommand<Guid>;

internal sealed class SaveGlossaryEntryValidator : AbstractValidator<SaveGlossaryEntryCommand>
{
    public SaveGlossaryEntryValidator()
    {
        RuleFor(c => c.TermAr).NotEmpty().MaximumLength(GlossaryEntry.TermMaxLength);
        RuleFor(c => c.TermEn).NotEmpty().MaximumLength(GlossaryEntry.TermMaxLength);
        RuleFor(c => c.DefinitionAr).NotEmpty().MaximumLength(GlossaryEntry.DefinitionMaxLength);
        RuleFor(c => c.DefinitionEn).NotEmpty().MaximumLength(GlossaryEntry.DefinitionMaxLength);
    }
}

internal sealed class SaveGlossaryEntryHandler(GlossaryGate gate, IGlossaryRepository glossary, ICurrentUser user, ILearningUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<SaveGlossaryEntryCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveGlossaryEntryCommand request, CancellationToken cancellationToken)
    {
        var manage = await gate.EnterAsync(request.CourseId, cancellationToken);
        if (!manage.IsSuccess)
        {
            return manage.Error!;
        }

        var term = LocalizedText.Create(request.TermAr, request.TermEn, GlossaryEntry.TermMaxLength);
        var definition = LocalizedText.Create(request.DefinitionAr, request.DefinitionEn, GlossaryEntry.DefinitionMaxLength);
        if (!term.IsSuccess || !definition.IsSuccess)
        {
            return (term.Error ?? definition.Error)!;
        }

        if (request.Id is { } id)
        {
            var entry = await glossary.FindAsync(id, cancellationToken);
            if (entry is null || entry.CourseId != request.CourseId)
            {
                return ReadingErrors.EntryNotFound;
            }

            // A trainee may fix their own suggestion only while it waits for review.
            if (!manage.Value && (entry.AuthorId != user.Id || entry.IsApproved))
            {
                return ContentErrors.Forbidden;
            }

            entry.Update(term.Value, definition.Value);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return id;
        }

        var created = GlossaryEntry.Create(request.CourseId, term.Value, definition.Value, manage.Value, user.Id!.Value, clock.GetUtcNow());
        glossary.Add(created);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return created.Id;
    }
}

public sealed record ApproveGlossaryEntryCommand(Guid CourseId, Guid Id) : ICommand<Unit>;

internal sealed class ApproveGlossaryEntryHandler(GlossaryGate gate, IGlossaryRepository glossary, ILearningUnitOfWork unitOfWork) : IRequestHandler<ApproveGlossaryEntryCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ApproveGlossaryEntryCommand request, CancellationToken cancellationToken)
    {
        var manage = await gate.EnterAsync(request.CourseId, cancellationToken);
        if (!manage.IsSuccess)
        {
            return manage.Error!;
        }

        if (!manage.Value)
        {
            return ContentErrors.Forbidden;
        }

        var entry = await glossary.FindAsync(request.Id, cancellationToken);
        if (entry is null || entry.CourseId != request.CourseId)
        {
            return ReadingErrors.EntryNotFound;
        }

        entry.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record RemoveGlossaryEntryCommand(Guid CourseId, Guid Id) : ICommand<Unit>;

internal sealed class RemoveGlossaryEntryHandler(GlossaryGate gate, IGlossaryRepository glossary, ICurrentUser user, ILearningUnitOfWork unitOfWork) : IRequestHandler<RemoveGlossaryEntryCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RemoveGlossaryEntryCommand request, CancellationToken cancellationToken)
    {
        var manage = await gate.EnterAsync(request.CourseId, cancellationToken);
        if (!manage.IsSuccess)
        {
            return manage.Error!;
        }

        var entry = await glossary.FindAsync(request.Id, cancellationToken);
        if (entry is null || entry.CourseId != request.CourseId)
        {
            return ReadingErrors.EntryNotFound;
        }

        if (!manage.Value && (entry.AuthorId != user.Id || entry.IsApproved))
        {
            return ContentErrors.Forbidden;
        }

        glossary.Remove(entry);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
