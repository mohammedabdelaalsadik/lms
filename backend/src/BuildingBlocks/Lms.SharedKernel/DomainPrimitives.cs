namespace Lms.SharedKernel;

public interface IDomainEvent;

public abstract class Entity<TId> where TId : struct
{
    public TId Id { get; protected init; }
}

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}

/// <summary>Consistency boundary. Only aggregates raise domain events and own repositories.</summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>Text that must exist in both supported languages.</summary>
public sealed record LocalizedText(string Ar, string En)
{
    public static Result<LocalizedText> Create(string? ar, string? en, int maxLength)
    {
        ar = ar?.Trim();
        en = en?.Trim();

        if (string.IsNullOrEmpty(ar) || string.IsNullOrEmpty(en))
        {
            return CommonErrors.LocalizedTextRequired;
        }

        if (ar.Length > maxLength || en.Length > maxLength)
        {
            return CommonErrors.LocalizedTextTooLong;
        }

        return new LocalizedText(ar, en);
    }
}

public static class CommonErrors
{
    public static readonly Error LocalizedTextRequired =
        Error.Validation("common.localized_text.required", "Text is required in Arabic and English.");

    public static readonly Error LocalizedTextTooLong =
        Error.Validation("common.localized_text.too_long", "Text is too long.");
}
