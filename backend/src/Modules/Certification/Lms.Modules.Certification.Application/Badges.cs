using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Catalog.Contracts;
using Lms.Modules.Certification.Domain;
using Lms.SharedKernel;

namespace Lms.Modules.Certification.Application;

public interface IBadgeRepository
{
    Task<Badge?> FindAsync(BadgeId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Badge>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Badge>> ListLiveForCourseAsync(Guid courseId, CancellationToken cancellationToken);

    Task<IReadOnlyList<(Badge Badge, BadgeAward Award)>> ListForTraineeAsync(Guid traineeId, CancellationToken cancellationToken);

    Task<bool> HasAwardAsync(BadgeId id, Guid traineeId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<BadgeId, int>> CountAwardsAsync(CancellationToken cancellationToken);

    Task RemoveAwardsAsync(Guid courseId, Guid traineeId, CancellationToken cancellationToken);

    void Add(Badge badge);

    void Add(BadgeAward award);
}

/// <summary>Called with a certificate, in the same save, so the two can never disagree.</summary>
public sealed class BadgeAwarder(IBadgeRepository badges, TimeProvider clock)
{
    public async Task AwardAsync(Guid courseId, Guid traineeId, CancellationToken cancellationToken)
    {
        foreach (var badge in await badges.ListLiveForCourseAsync(courseId, cancellationToken))
        {
            if (!await badges.HasAwardAsync(badge.Id, traineeId, cancellationToken))
            {
                badges.Add(BadgeAward.Create(badge, traineeId, clock.GetUtcNow()));
            }
        }
    }
}

public sealed record BadgeResponse(Guid Id, Guid CourseId, string CourseCode, TextDto Name, TextDto Description, string Icon, bool IsRetired, int Awarded);

public sealed record ListBadgesQuery : IQuery<IReadOnlyList<BadgeResponse>>;

internal sealed class ListBadgesHandler(IBadgeRepository badges, ICourseCatalog catalog) : IRequestHandler<ListBadgesQuery, IReadOnlyList<BadgeResponse>>
{
    public async Task<Result<IReadOnlyList<BadgeResponse>>> Handle(ListBadgesQuery request, CancellationToken cancellationToken)
    {
        var list = await badges.ListAsync(cancellationToken);
        var counts = await badges.CountAwardsAsync(cancellationToken);
        var courses = await catalog.GetAsync([.. list.Select(b => b.CourseId).Distinct()], cancellationToken);

        IReadOnlyList<BadgeResponse> items =
        [
            .. list.Select(b => new BadgeResponse(
                b.Id.Value,
                b.CourseId,
                courses.GetValueOrDefault(b.CourseId)?.Code ?? "",
                new TextDto(b.Name.Ar, b.Name.En),
                new TextDto(b.Description.Ar, b.Description.En),
                b.Icon,
                b.RetiredAt is not null,
                counts.GetValueOrDefault(b.Id))),
        ];

        return Result<IReadOnlyList<BadgeResponse>>.Success(items);
    }
}

public sealed record CreateBadgeCommand(Guid CourseId, string NameAr, string NameEn, string DescriptionAr, string DescriptionEn, string Icon) : ICommand<Guid>;

internal sealed class CreateBadgeValidator : AbstractValidator<CreateBadgeCommand>
{
    public CreateBadgeValidator()
    {
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(Badge.NameMaxLength);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(Badge.NameMaxLength);
        RuleFor(c => c.DescriptionAr).NotEmpty().MaximumLength(Badge.DescriptionMaxLength);
        RuleFor(c => c.DescriptionEn).NotEmpty().MaximumLength(Badge.DescriptionMaxLength);
    }
}

internal sealed class CreateBadgeHandler(IBadgeRepository badges, ICourseCatalog catalog, ICertificationUnitOfWork unitOfWork) : IRequestHandler<CreateBadgeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateBadgeCommand request, CancellationToken cancellationToken)
    {
        var course = await catalog.GetAsync([request.CourseId], cancellationToken);
        var name = LocalizedText.Create(request.NameAr, request.NameEn, Badge.NameMaxLength);
        var description = LocalizedText.Create(request.DescriptionAr, request.DescriptionEn, Badge.DescriptionMaxLength);

        if (course.Count == 0)
        {
            return CertificateErrors.SourceMissing;
        }

        if (!name.IsSuccess || !description.IsSuccess)
        {
            return (name.Error ?? description.Error)!;
        }

        var badge = Badge.Create(request.CourseId, name.Value, description.Value, request.Icon);
        if (!badge.IsSuccess)
        {
            return badge.Error!;
        }

        badges.Add(badge.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return badge.Value.Id.Value;
    }
}

public sealed record RetireBadgeCommand(Guid Id) : ICommand<Unit>;

internal sealed class RetireBadgeHandler(IBadgeRepository badges, ICertificationUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<RetireBadgeCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RetireBadgeCommand request, CancellationToken cancellationToken)
    {
        var badge = await badges.FindAsync(new BadgeId(request.Id), cancellationToken);
        if (badge is null)
        {
            return BadgeErrors.NotFound;
        }

        badge.Retire(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record MyBadge(Guid Id, TextDto Name, TextDto Description, string Icon, DateTimeOffset AwardedAt);

public sealed record ListMyBadgesQuery : IQuery<IReadOnlyList<MyBadge>>;

internal sealed class ListMyBadgesHandler(IBadgeRepository badges, ICurrentUser user) : IRequestHandler<ListMyBadgesQuery, IReadOnlyList<MyBadge>>
{
    public async Task<Result<IReadOnlyList<MyBadge>>> Handle(ListMyBadgesQuery request, CancellationToken cancellationToken)
    {
        var list = await badges.ListForTraineeAsync(user.Id!.Value, cancellationToken);
        IReadOnlyList<MyBadge> items = [.. list.Select(x => new MyBadge(x.Badge.Id.Value, new TextDto(x.Badge.Name.Ar, x.Badge.Name.En), new TextDto(x.Badge.Description.Ar, x.Badge.Description.En), x.Badge.Icon, x.Award.AwardedAt))];
        return Result<IReadOnlyList<MyBadge>>.Success(items);
    }
}
