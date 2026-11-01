namespace Lms.BuildingBlocks.Application;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICurrentUser
{
    Guid? Id { get; }

    string? Role { get; }

    string? IpAddress { get; }
}

public static class LmsRoles
{
    public const string Trainee = nameof(Trainee);
    public const string Instructor = nameof(Instructor);
    public const string Coordinator = nameof(Coordinator);
    public const string Admin = nameof(Admin);
    public const string Auditor = nameof(Auditor);

    public const string CatalogManagers = Admin + "," + Coordinator;
    public const string UserViewers = Admin + "," + Auditor;
    public const string CohortViewers = CatalogManagers + "," + Instructor;
    public const string CertificateViewers = CatalogManagers + "," + Auditor;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
