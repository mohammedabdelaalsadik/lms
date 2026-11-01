using Lms.SharedKernel;

namespace Lms.Modules.Learning.Domain;

public sealed record BookChapter(string TitleAr, string TitleEn, string BodyAr, string BodyEn);

public sealed record BookDefinition(IReadOnlyList<BookChapter> Chapters)
{
    public const int MaxChapters = 50;

    public bool IsValid =>
        Chapters is { Count: > 0 and <= MaxChapters }
        && Chapters.All(c => Reading.Title(c.TitleAr) && Reading.Title(c.TitleEn) && Reading.Body(c.BodyAr) && Reading.Body(c.BodyEn));
}

/// <param name="Target">The page to open, counted from zero. Null ends the lesson.</param>
public sealed record LessonChoice(string Ar, string En, int? Target);

/// <summary>A page with no choices simply continues to the next page, and the last page ends the lesson.</summary>
public sealed record LessonPage(string TitleAr, string TitleEn, string BodyAr, string BodyEn, IReadOnlyList<LessonChoice>? Choices);

public sealed record LessonDefinition(IReadOnlyList<LessonPage> Pages)
{
    public const int MaxPages = 50;
    public const int MaxChoices = 6;

    public bool IsValid =>
        Pages is { Count: > 0 and <= MaxPages }
        && Pages.All(PageFits)
        && EndIsReachable();

    private bool PageFits(LessonPage page) =>
        Reading.Title(page.TitleAr) && Reading.Title(page.TitleEn) && Reading.Body(page.BodyAr) && Reading.Body(page.BodyEn)
        && (page.Choices?.Count ?? 0) <= MaxChoices
        && (page.Choices ?? []).All(c => Reading.Title(c.Ar) && Reading.Title(c.En) && (c.Target is null || (c.Target >= 0 && c.Target < Pages.Count)));

    /// <summary>A lesson whose every route loops could never be finished.</summary>
    private bool EndIsReachable()
    {
        var seen = new HashSet<int> { 0 };
        var waiting = new Queue<int>([0]);

        while (waiting.TryDequeue(out var index))
        {
            var choices = Pages[index].Choices ?? [];
            int?[] next = choices.Count > 0 ? [.. choices.Select(c => c.Target)] : [index + 1 < Pages.Count ? index + 1 : null];

            foreach (var target in next)
            {
                if (target is null)
                {
                    return true;
                }

                if (seen.Add(target.Value))
                {
                    waiting.Enqueue(target.Value);
                }
            }
        }

        return false;
    }
}

internal static class Reading
{
    public static bool Title(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= Activity.TitleMaxLength;

    public static bool Body(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= Activity.BodyMaxLength;
}

public static class ReadingErrors
{
    public static readonly Error InvalidBook = Error.Validation("learning.book_invalid", "Each chapter needs a title and text in both languages.");
    public static readonly Error InvalidLesson = Error.Validation("learning.lesson_invalid", "Each page needs a title and text in both languages, and the lesson must be able to end.");
    public static readonly Error EntryNotFound = Error.NotFound("learning.glossary_not_found", "Glossary entry was not found.");
}

/// <summary>A term for a whole course. Trainees may suggest one, and it stays private to them until staff approve it.</summary>
public sealed class GlossaryEntry
{
    public const int TermMaxLength = 200;
    public const int DefinitionMaxLength = 4000;

    private GlossaryEntry()
    {
    }

    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public LocalizedText Term { get; private set; } = null!;
    public LocalizedText Definition { get; private set; } = null!;
    public bool IsApproved { get; private set; }
    public Guid AuthorId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static GlossaryEntry Create(Guid courseId, LocalizedText term, LocalizedText definition, bool approved, Guid authorId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        CourseId = courseId,
        Term = term,
        Definition = definition,
        IsApproved = approved,
        AuthorId = authorId,
        CreatedAt = now,
    };

    public void Update(LocalizedText term, LocalizedText definition)
    {
        Term = term;
        Definition = definition;
    }

    public void Approve() => IsApproved = true;
}
