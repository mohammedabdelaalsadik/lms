using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Audit.Domain;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Audit.Application;

public sealed record AuditFilter(Guid? ActorId, string? Search, DateOnly? From, DateOnly? To, int Page, int PageSize);

public interface IAuditRepository
{
    void Add(AuditEntry entry);

    Task SaveAsync(CancellationToken cancellationToken);

    Task<PagedResult<AuditEntry>> ListAsync(AuditFilter filter, CancellationToken cancellationToken);
}

public sealed class AuditTrail(IAuditRepository entries) : IAuditTrail
{
    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        entries.Add(AuditEntry.Create(
            record.OccurredAt,
            record.ActorId,
            record.ActorRole,
            record.IpAddress,
            record.Action,
            record.Details,
            record.Succeeded,
            record.ErrorCode));

        await entries.SaveAsync(cancellationToken);
    }
}

public sealed record TextDto(string Ar, string En);

public sealed record AuditEntryResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorId,
    TextDto? ActorName,
    string? ActorRole,
    string? IpAddress,
    string Action,
    string Details,
    bool Succeeded,
    string? ErrorCode);

public sealed record ListAuditEntriesQuery(Guid? ActorId, string? Search, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<AuditEntryResponse>>;

internal sealed class ListAuditEntriesValidator : AbstractValidator<ListAuditEntriesQuery>
{
    public ListAuditEntriesValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.Search).MaximumLength(100);
    }
}

internal sealed class ListAuditEntriesHandler(IAuditRepository entries, IUserDirectory directory)
    : IRequestHandler<ListAuditEntriesQuery, PagedResult<AuditEntryResponse>>
{
    public async Task<Result<PagedResult<AuditEntryResponse>>> Handle(ListAuditEntriesQuery request, CancellationToken cancellationToken)
    {
        var filter = new AuditFilter(request.ActorId, request.Search, request.From, request.To, request.Page, request.PageSize);
        var page = await entries.ListAsync(filter, cancellationToken);
        var actors = page.Items.Where(e => e.ActorId is not null).Select(e => e.ActorId!.Value).Distinct().ToList();
        var names = await directory.GetAsync(actors, cancellationToken);

        return new PagedResult<AuditEntryResponse>(
            [.. page.Items.Select(e =>
            {
                var actor = e.ActorId is { } id ? names.GetValueOrDefault(id) : null;

                return new AuditEntryResponse(
                    e.Id,
                    e.OccurredAt,
                    e.ActorId,
                    actor is null ? null : new TextDto(actor.NameAr, actor.NameEn),
                    e.ActorRole,
                    e.IpAddress,
                    e.Action,
                    e.Details,
                    e.Succeeded,
                    e.ErrorCode);
            })],
            page.Total,
            page.Page,
            page.PageSize);
    }
}

public sealed record ExportAuditEntriesQuery(string? Search, DateOnly? From, DateOnly? To) : IQuery<CsvFile>, IAuditedRequest;

internal sealed class ExportAuditEntriesHandler(IRequestHandler<ListAuditEntriesQuery, PagedResult<AuditEntryResponse>> entries) : IRequestHandler<ExportAuditEntriesQuery, CsvFile>
{
    private const int PageSize = 100;
    private const int MaxPages = 100;

    public async Task<Result<CsvFile>> Handle(ExportAuditEntriesQuery request, CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyList<string?>> { new[] { "الوقت / Time", "المستخدم / User", "الدور / Role", "IP", "الإجراء / Action", "التفاصيل / Details", "نجح / Succeeded", "الخطأ / Error" } };

        for (var page = 1; page <= MaxPages; page++)
        {
            var batch = await entries.Handle(new ListAuditEntriesQuery(null, request.Search, request.From, request.To, page, PageSize), cancellationToken);
            if (!batch.IsSuccess)
            {
                return batch.Error!;
            }

            rows.AddRange(batch.Value.Items.Select(e => (IReadOnlyList<string?>)
            [
                e.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                e.ActorName?.En,
                e.ActorRole,
                e.IpAddress,
                e.Action,
                e.Details,
                e.Succeeded ? "yes" : "no",
                e.ErrorCode,
            ]));

            if (page * PageSize >= batch.Value.Total)
            {
                break;
            }
        }

        return CsvWriter.Write("audit-log.csv", rows);
    }
}
