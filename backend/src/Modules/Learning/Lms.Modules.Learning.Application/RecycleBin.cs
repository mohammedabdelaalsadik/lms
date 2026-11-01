using Lms.BuildingBlocks.Application;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;
using System.Text.Json;

namespace Lms.Modules.Learning.Application;

public interface IRecycleBinRepository
{
    Task<BinItem?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<BinItem>> ListAsync(Guid courseId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BinItem>> ListRemovedBeforeAsync(DateTimeOffset cutoff, int take, CancellationToken cancellationToken);

    void Add(BinItem item);

    void Remove(BinItem item);
}

/// <summary>Puts removed content in the bin in the same save, and finds which files an item owns.</summary>
public sealed class RecycleBin(IRecycleBinRepository bin, ICurrentUser user, TimeProvider clock)
{
    public void Hold(Guid courseId, SectionSnapshot section) =>
        bin.Add(BinItem.Create(courseId, BinItemKind.Section, section.TitleAr, section.TitleEn, null, JsonSerializer.Serialize(section), user.Id!.Value, clock.GetUtcNow()));

    public void Hold(Guid courseId, SectionId sectionId, ActivitySnapshot activity) =>
        bin.Add(BinItem.Create(courseId, BinItemKind.Activity, activity.TitleAr, activity.TitleEn, sectionId.Value, JsonSerializer.Serialize(activity), user.Id!.Value, clock.GetUtcNow()));

    public static IReadOnlyList<string> FilesOf(BinItem item)
    {
        var activities = item.Kind == BinItemKind.Section
            ? JsonSerializer.Deserialize<SectionSnapshot>(item.Payload)!.Activities
            : [JsonSerializer.Deserialize<ActivitySnapshot>(item.Payload)!];

        return [.. activities.Where(a => a.FileKey is not null).Select(a => a.FileKey!)];
    }
}

public sealed record BinEntry(Guid Id, BinItemKind Kind, TextDto Title, DateTimeOffset RemovedAt, DateTimeOffset ExpiresAt);

public sealed record ListBinQuery(Guid CourseId) : IQuery<IReadOnlyList<BinEntry>>;

internal sealed class ListBinHandler(ContentEditor editor, IRecycleBinRepository bin) : IRequestHandler<ListBinQuery, IReadOnlyList<BinEntry>>
{
    public async Task<Result<IReadOnlyList<BinEntry>>> Handle(ListBinQuery request, CancellationToken cancellationToken)
    {
        var content = await editor.OpenAsync(request.CourseId, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        var items = await bin.ListAsync(request.CourseId, cancellationToken);
        IReadOnlyList<BinEntry> entries = [.. items.Select(i => new BinEntry(i.Id, i.Kind, new TextDto(i.TitleAr, i.TitleEn), i.RemovedAt, i.ExpiresAt))];
        return Result<IReadOnlyList<BinEntry>>.Success(entries);
    }
}

public sealed record RestoreBinItemCommand(Guid CourseId, Guid ItemId) : ICommand<CourseContentResponse>;

internal sealed class RestoreBinItemHandler(ContentEditor editor, IRecycleBinRepository bin) : IRequestHandler<RestoreBinItemCommand, CourseContentResponse>
{
    public async Task<Result<CourseContentResponse>> Handle(RestoreBinItemCommand request, CancellationToken cancellationToken)
    {
        var content = await editor.OpenAsync(request.CourseId, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        var item = await bin.FindAsync(request.ItemId, cancellationToken);
        if (item is null || item.CourseId != request.CourseId)
        {
            return BinErrors.NotFound;
        }

        var outcome = item.Kind == BinItemKind.Section
            ? content.Value.RestoreSection(JsonSerializer.Deserialize<SectionSnapshot>(item.Payload)!)
            : content.Value.RestoreActivity(JsonSerializer.Deserialize<ActivitySnapshot>(item.Payload)!, new SectionId(item.SectionId!.Value));

        if (outcome.IsSuccess)
        {
            bin.Remove(item);
        }

        return await editor.FinishAsync(content.Value, outcome, cancellationToken);
    }
}

public sealed record PurgeBinItemCommand(Guid CourseId, Guid ItemId) : ICommand<Unit>;

internal sealed class PurgeBinItemHandler(LearningAccess access, IRecycleBinRepository bin, IFileStorage storage, ILearningUnitOfWork unitOfWork) : IRequestHandler<PurgeBinItemCommand, Unit>
{
    public async Task<Result<Unit>> Handle(PurgeBinItemCommand request, CancellationToken cancellationToken)
    {
        if (!access.IsManager)
        {
            return ContentErrors.Forbidden;
        }

        var item = await bin.FindAsync(request.ItemId, cancellationToken);
        if (item is null || item.CourseId != request.CourseId)
        {
            return BinErrors.NotFound;
        }

        var files = RecycleBin.FilesOf(item);
        bin.Remove(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var key in files)
        {
            storage.Delete(key);
        }

        return default(Unit);
    }
}
