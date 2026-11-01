using Lms.BuildingBlocks.Application;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;

namespace Lms.Modules.Learning.Application;

public interface IPrivateFileRepository
{
    Task<PrivateFile?> FindAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrivateFile>> ListAsync(Guid ownerId, CancellationToken cancellationToken);

    void Add(PrivateFile file);

    void Remove(PrivateFile file);
}

public sealed record PrivateFileItem(Guid Id, string FileName, long Size, DateTimeOffset UploadedAt);

public sealed record PrivateFilesResponse(long Used, long Quota, IReadOnlyList<PrivateFileItem> Files);

public sealed record ListMyFilesQuery : IQuery<PrivateFilesResponse>;

internal sealed class ListMyFilesHandler(IPrivateFileRepository files, ICurrentUser user) : IRequestHandler<ListMyFilesQuery, PrivateFilesResponse>
{
    public async Task<Result<PrivateFilesResponse>> Handle(ListMyFilesQuery request, CancellationToken cancellationToken)
    {
        var list = await files.ListAsync(user.Id!.Value, cancellationToken);
        return new PrivateFilesResponse(list.Sum(f => f.Size), PrivateFile.QuotaBytes, [.. list.Select(f => new PrivateFileItem(f.Id, f.FileName, f.Size, f.UploadedAt))]);
    }
}

public sealed record UploadMyFileCommand(string FileName, long Size, Stream Content) : ICommand<Guid>;

internal sealed class UploadMyFileHandler(IPrivateFileRepository files, IFileStorage storage, ICurrentUser user, ILearningUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<UploadMyFileCommand, Guid>
{
    public async Task<Result<Guid>> Handle(UploadMyFileCommand request, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(request.FileName);
        var contentType = FileRules.ContentTypeFor(fileName);

        if (contentType is null || fileName.Length > 255)
        {
            return FileErrors.TypeNotAllowed;
        }

        if (request.Size is <= 0 or > FileRules.MaxSize)
        {
            return FileErrors.TooLarge;
        }

        var owner = user.Id!.Value;
        var used = (await files.ListAsync(owner, cancellationToken)).Sum(f => f.Size);
        if (!PrivateFile.Fits(used, request.Size))
        {
            return PrivateFileErrors.QuotaExceeded;
        }

        var key = await storage.SaveAsync(request.Content, cancellationToken);
        var file = PrivateFile.Create(owner, new FileAsset(key, fileName, contentType, request.Size), clock.GetUtcNow());
        files.Add(file);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return file.Id;
    }
}

public sealed record DownloadMyFileQuery(Guid Id) : IQuery<ActivityFile>;

internal sealed class DownloadMyFileHandler(IPrivateFileRepository files, IFileStorage storage, ICurrentUser user) : IRequestHandler<DownloadMyFileQuery, ActivityFile>
{
    public async Task<Result<ActivityFile>> Handle(DownloadMyFileQuery request, CancellationToken cancellationToken)
    {
        var file = await files.FindAsync(request.Id, user.Id!.Value, cancellationToken);
        return file is null ? PrivateFileErrors.NotFound : new ActivityFile(storage.Open(file.StorageKey), file.FileName, file.ContentType);
    }
}

public sealed record RemoveMyFileCommand(Guid Id) : ICommand<Unit>;

internal sealed class RemoveMyFileHandler(IPrivateFileRepository files, IFileStorage storage, ICurrentUser user, ILearningUnitOfWork unitOfWork) : IRequestHandler<RemoveMyFileCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RemoveMyFileCommand request, CancellationToken cancellationToken)
    {
        var file = await files.FindAsync(request.Id, user.Id!.Value, cancellationToken);
        if (file is null)
        {
            return PrivateFileErrors.NotFound;
        }

        files.Remove(file);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        storage.Delete(file.StorageKey);
        return default(Unit);
    }
}
