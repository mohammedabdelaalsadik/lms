using Lms.SharedKernel;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Lms.BuildingBlocks.Application;

/// <summary>Marks a read that must leave a trace, such as seeing a full National ID or a person's file.</summary>
public interface IAuditedRequest;

/// <summary>Excludes routine, high-volume commands such as saving a quiz answer.</summary>
public interface INotAudited;

/// <summary>A response naming the acting user when the session cannot yet, as at sign-in.</summary>
public interface IAuditActor
{
    Guid ActorId { get; }
}

public sealed record AuditRecord(
    DateTimeOffset OccurredAt,
    Guid? ActorId,
    string? ActorRole,
    string? IpAddress,
    string Action,
    string Details,
    bool Succeeded,
    string? ErrorCode);

public interface IAuditTrail
{
    Task RecordAsync(AuditRecord record, CancellationToken cancellationToken);
}

/// <summary>Writes one audit entry for each command and each audited read.</summary>
internal sealed class AuditRecorder(IAuditTrail trail, ICurrentUser user, TimeProvider clock, ILogger<AuditRecorder> logger)
{
    private const int MaxDetailsLength = 2000;

    /// <summary>Deny by default: other text, such as names or passwords, is never copied into the log.</summary>
    private static readonly HashSet<string> AllowedText = new(StringComparer.Ordinal) { "Reason" };

    /// <summary>Keeps Arabic reasons readable and searchable. The value is stored as data, never as markup.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly ConcurrentDictionary<Type, bool> Audited = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Loggable = new();

    public async Task RecordAsync<TRequest, TResponse>(TRequest request, Result<TResponse> result, CancellationToken cancellationToken)
        where TRequest : notnull
    {
        var type = request.GetType();

        if (!Audited.GetOrAdd(type, IsAudited))
        {
            return;
        }

        var actor = result.IsSuccess && result.Value is IAuditActor named ? named.ActorId : user.Id;
        var record = new AuditRecord(
            clock.GetUtcNow(),
            actor,
            user.Role,
            user.IpAddress,
            ActionName(type),
            Describe(request, type),
            result.IsSuccess,
            result.Error?.Code);

        try
        {
            await trail.RecordAsync(record, cancellationToken);
        }
        catch (Exception exception) when (request is not IAuditedRequest)
        {
            // The change is already saved, so reporting failure would mislead the user.
            // Audited reads are not caught here: with no trace written, nothing is returned.
            logger.LogCritical(exception, "Audit entry could not be written for {Action}", record.Action);
        }
    }

    private static bool IsAudited(Type type)
    {
        if (typeof(INotAudited).IsAssignableFrom(type))
        {
            return false;
        }

        return typeof(IAuditedRequest).IsAssignableFrom(type)
            || Array.Exists(type.GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
    }

    private static string ActionName(Type type)
    {
        foreach (var suffix in new[] { "Command", "Query" })
        {
            if (type.Name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return type.Name[..^suffix.Length];
            }
        }

        return type.Name;
    }

    private static string Describe(object request, Type type)
    {
        var properties = Loggable.GetOrAdd(type, static t => [.. t.GetProperties().Where(IsLoggable)]);
        var values = properties.ToDictionary(p => p.Name, p => Readable(p.GetValue(request)));
        var json = JsonSerializer.Serialize(values, JsonOptions);
        return json.Length > MaxDetailsLength ? json[..MaxDetailsLength] : json;
    }

    private static object? Readable(object? value) => value is Enum ? value.ToString() : value;

    private static bool IsLoggable(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type == typeof(string))
        {
            return AllowedText.Contains(property.Name);
        }

        return type.IsEnum
            || type == typeof(Guid)
            || type == typeof(int)
            || type == typeof(long)
            || type == typeof(decimal)
            || type == typeof(bool)
            || type == typeof(DateOnly)
            || type == typeof(DateTimeOffset);
    }
}
