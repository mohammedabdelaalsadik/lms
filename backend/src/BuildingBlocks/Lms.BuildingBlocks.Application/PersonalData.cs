namespace Lms.BuildingBlocks.Application;

public sealed record PersonalDataSection(string Name, object Data);

/// <summary>
/// Every module that stores something about a person registers one of these. A data export reads them all.
/// Never return secrets, unpublished grades, accepted answers or a full National ID.
/// </summary>
public interface IPersonalDataSource
{
    Task<PersonalDataSection> ReadAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Must be safe to repeat. Modules whose records must be kept leave this as it is.</summary>
    Task EraseAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
}
