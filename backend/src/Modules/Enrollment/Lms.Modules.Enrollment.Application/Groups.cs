using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Enrollment.Domain;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;
using System.Security.Cryptography;

namespace Lms.Modules.Enrollment.Application;

public interface ICohortGroupRepository
{
    Task<IReadOnlyList<CohortGroup>> ListAsync(Guid cohortId, CancellationToken cancellationToken);

    void Add(CohortGroup group);

    void Remove(CohortGroup group);
}

public sealed class GroupAccess(ICohortRepository cohorts, ICurrentUser user)
{
    public async Task<Result<Cohort>> ManageAsync(Guid cohortId, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(new CohortId(cohortId), cancellationToken);
        if (cohort is null)
        {
            return EnrollmentErrors.CohortNotFound;
        }

        return user.IsManager() || cohort.InstructorId == user.Id ? cohort : EnrollmentErrors.Forbidden;
    }
}

public sealed record GroupPerson(Guid TraineeId, TextDto Name);

public sealed record GroupResponse(Guid Id, string Name, IReadOnlyList<GroupPerson> Members);

public sealed record GroupBoardResponse(IReadOnlyList<GroupResponse> Groups, IReadOnlyList<GroupPerson> Unassigned);

public sealed record GetGroupBoardQuery(Guid CohortId) : IQuery<GroupBoardResponse>;

internal sealed class GetGroupBoardHandler(GroupAccess access, ICohortGroupRepository groups, IUserDirectory directory)
    : IRequestHandler<GetGroupBoardQuery, GroupBoardResponse>
{
    public async Task<Result<GroupBoardResponse>> Handle(GetGroupBoardQuery request, CancellationToken cancellationToken)
    {
        var cohort = await access.ManageAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var list = await groups.ListAsync(request.CohortId, cancellationToken);
        var enrolled = cohort.Value.Enrollments.Where(e => e.Status == EnrollmentStatus.Enrolled).Select(e => e.TraineeId).ToList();
        var people = await directory.GetAsync(enrolled, cancellationToken);

        GroupPerson Person(Guid id) => new(id, new TextDto(people.GetValueOrDefault(id)?.NameAr ?? "", people.GetValueOrDefault(id)?.NameEn ?? ""));

        // Someone who withdrew stays in the table but drops off the board.
        var placed = list.SelectMany(g => g.Members).Select(m => m.TraineeId).ToHashSet();

        return new GroupBoardResponse(
            [.. list.OrderBy(g => g.Name).Select(g => new GroupResponse(g.Id.Value, g.Name, [.. g.Members.Where(m => enrolled.Contains(m.TraineeId)).Select(m => Person(m.TraineeId))]))],
            [.. enrolled.Where(id => !placed.Contains(id)).Select(Person)]);
    }
}

public sealed record SaveGroupCommand(Guid CohortId, Guid? GroupId, string Name) : ICommand<Guid>;

internal sealed class SaveGroupValidator : AbstractValidator<SaveGroupCommand>
{
    public SaveGroupValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(CohortGroup.NameMaxLength).Matches(@"\S");
}

internal sealed class SaveGroupHandler(GroupAccess access, ICohortGroupRepository groups, IEnrollmentUnitOfWork unitOfWork) : IRequestHandler<SaveGroupCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveGroupCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.ManageAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var list = await groups.ListAsync(request.CohortId, cancellationToken);

        if (list.Any(g => g.Id.Value != request.GroupId && string.Equals(g.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return EnrollmentErrors.GroupNameTaken;
        }

        if (request.GroupId is { } id)
        {
            var group = list.FirstOrDefault(g => g.Id.Value == id);
            if (group is null)
            {
                return EnrollmentErrors.GroupNotFound;
            }

            group.Rename(request.Name);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return id;
        }

        var created = CohortGroup.Create(request.CohortId, request.Name);
        groups.Add(created);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return created.Id.Value;
    }
}

public sealed record DeleteGroupCommand(Guid CohortId, Guid GroupId) : ICommand<Unit>;

internal sealed class DeleteGroupHandler(GroupAccess access, ICohortGroupRepository groups, IEnrollmentUnitOfWork unitOfWork) : IRequestHandler<DeleteGroupCommand, Unit>
{
    public async Task<Result<Unit>> Handle(DeleteGroupCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.ManageAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var group = (await groups.ListAsync(request.CohortId, cancellationToken)).FirstOrDefault(g => g.Id.Value == request.GroupId);
        if (group is null)
        {
            return EnrollmentErrors.GroupNotFound;
        }

        groups.Remove(group);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

/// <summary>Moves one trainee. An empty group takes them out of every group.</summary>
public sealed record PlaceTraineeCommand(Guid CohortId, Guid TraineeId, Guid? GroupId) : ICommand<Unit>;

internal sealed class PlaceTraineeHandler(GroupAccess access, ICohortGroupRepository groups, IEnrollmentUnitOfWork unitOfWork) : IRequestHandler<PlaceTraineeCommand, Unit>
{
    public async Task<Result<Unit>> Handle(PlaceTraineeCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.ManageAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        if (!cohort.Value.Enrollments.Any(e => e.TraineeId == request.TraineeId && e.Status == EnrollmentStatus.Enrolled))
        {
            return EnrollmentErrors.EnrollmentNotFound;
        }

        var list = await groups.ListAsync(request.CohortId, cancellationToken);
        var target = request.GroupId is null ? null : list.FirstOrDefault(g => g.Id.Value == request.GroupId);

        if (request.GroupId is not null && target is null)
        {
            return EnrollmentErrors.GroupNotFound;
        }

        foreach (var group in list.Where(g => g != target))
        {
            group.Remove(request.TraineeId);
        }

        target?.Add(request.TraineeId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

/// <summary>Replaces every group in the cohort with this many, dealt at random.</summary>
public sealed record SplitIntoGroupsCommand(Guid CohortId, int Count) : ICommand<Unit>;

internal sealed class SplitIntoGroupsValidator : AbstractValidator<SplitIntoGroupsCommand>
{
    public SplitIntoGroupsValidator() => RuleFor(c => c.Count).InclusiveBetween(1, 50);
}

internal sealed class SplitIntoGroupsHandler(GroupAccess access, ICohortGroupRepository groups, IEnrollmentUnitOfWork unitOfWork) : IRequestHandler<SplitIntoGroupsCommand, Unit>
{
    public async Task<Result<Unit>> Handle(SplitIntoGroupsCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.ManageAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var existing = await groups.ListAsync(request.CohortId, cancellationToken);
        foreach (var group in existing)
        {
            group.Clear();
        }

        var list = existing.ToList();

        for (var i = list.Count; i < request.Count; i++)
        {
            var created = CohortGroup.Create(request.CohortId, $"{i + 1}");
            groups.Add(created);
            list.Add(created);
        }

        var trainees = cohort.Value.Enrollments.Where(e => e.Status == EnrollmentStatus.Enrolled).Select(e => e.TraineeId).ToArray();
        RandomNumberGenerator.Shuffle<Guid>(trainees);
        CohortGroup.Deal([.. list.Take(request.Count)], trainees);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
