using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Community.Application;
using Lms.Modules.Community.Domain;
using Lms.Modules.Identity.Contracts;
using Lms.Modules.Notifications.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Lms.Modules.Community.Infrastructure;

public sealed class CommunityDbContext(DbContextOptions<CommunityDbContext> options, TimeProvider clock)
    : ModuleDbContext(options, clock), ICommunityUnitOfWork
{
    public const string SchemaName = "community";

    protected override string Schema => SchemaName;

    public DbSet<ForumTopic> Topics => Set<ForumTopic>();

    public DbSet<ForumPost> Posts => Set<ForumPost>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ChatMessage> Messages => Set<ChatMessage>();

    public DbSet<WikiPage> WikiPages => Set<WikiPage>();

    public DbSet<WikiRevision> WikiRevisions => Set<WikiRevision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WikiPage>(b =>
        {
            b.ToTable("WikiPages");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).HasConversion(id => id.Value, value => new WikiPageId(value)).ValueGeneratedNever();
            b.Property(p => p.Title).HasMaxLength(WikiPage.TitleMaxLength);
            b.Property(p => p.Body).HasMaxLength(WikiPage.BodyMaxLength);

            // Two people saving at the same moment must not both win.
            b.Property(p => p.Version).IsConcurrencyToken();
            b.HasIndex(p => new { p.CohortId, p.Title }).IsUnique().HasFilter("[RemovedAt] IS NULL");
        });

        modelBuilder.Entity<WikiRevision>(b =>
        {
            b.ToTable("WikiRevisions");
            b.HasKey(r => r.Id);
            b.Property(r => r.PageId).HasConversion(id => id.Value, value => new WikiPageId(value));
            b.Property(r => r.Title).HasMaxLength(WikiPage.TitleMaxLength);
            b.Property(r => r.Body).HasMaxLength(WikiPage.BodyMaxLength);
            b.HasIndex(r => new { r.PageId, r.Version }).IsUnique();
            b.HasIndex(r => r.EditorId);
        });

        modelBuilder.Entity<ForumTopic>(b =>
        {
            b.ToTable("Topics");
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).HasConversion(id => id.Value, value => new TopicId(value)).ValueGeneratedNever();
            b.Property(t => t.Title).HasMaxLength(ForumTopic.TitleMaxLength);
            b.Property(t => t.Body).HasMaxLength(ForumTopic.BodyMaxLength);
            b.Property<byte[]>("RowVersion").IsRowVersion();
            b.HasIndex(t => new { t.CohortId, t.IsPinned, t.LastActivityAt });
        });

        modelBuilder.Entity<ForumPost>(b =>
        {
            b.ToTable("Posts");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).HasConversion(id => id.Value, value => new PostId(value)).ValueGeneratedNever();
            b.Property(p => p.TopicId).HasConversion(id => id.Value, value => new TopicId(value));
            b.Property(p => p.Body).HasMaxLength(ForumTopic.BodyMaxLength);
            b.HasIndex(p => new { p.TopicId, p.CreatedAt });
        });

        modelBuilder.Entity<Conversation>(b =>
        {
            b.ToTable("Conversations");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion(id => id.Value, value => new ConversationId(value)).ValueGeneratedNever();
            b.HasIndex(c => new { c.UserA, c.UserB }).IsUnique();
            b.HasIndex(c => c.UserB);
        });

        modelBuilder.Entity<ChatMessage>(b =>
        {
            b.ToTable("Messages");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).HasConversion(id => id.Value, value => new MessageId(value)).ValueGeneratedNever();
            b.Property(m => m.ConversationId).HasConversion(id => id.Value, value => new ConversationId(value));
            b.Property(m => m.Body).HasMaxLength(ChatMessage.BodyMaxLength);
            b.HasIndex(m => new { m.ConversationId, m.SentAt });
        });
    }
}

internal sealed class ForumRepository(CommunityDbContext db) : IForumRepository
{
    public Task<ForumTopic?> FindTopicAsync(TopicId id, CancellationToken cancellationToken) =>
        db.Topics.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<ForumPost?> FindPostAsync(PostId id, CancellationToken cancellationToken) =>
        db.Posts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<PagedResult<ForumTopic>> ListTopicsAsync(TopicFilter filter, CancellationToken cancellationToken)
    {
        var query = db.Topics.AsNoTracking().Where(t => t.CohortId == filter.CohortId && t.RemovedAt == null);

        if (!filter.AllGroups)
        {
            query = query.Where(t => t.GroupId == null || t.GroupId == filter.GroupId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(t => t.Title.Contains(term) || t.Body.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(t => t.IsPinned)
            .ThenByDescending(t => t.LastActivityAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ForumTopic>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<IReadOnlyList<ForumPost>> ListPostsAsync(TopicId topicId, CancellationToken cancellationToken) =>
        await db.Posts.AsNoTracking().Where(p => p.TopicId == topicId).OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);

    public void Add(ForumTopic topic) => db.Topics.Add(topic);

    public void Add(ForumPost post) => db.Posts.Add(post);
}

internal sealed class ConversationRepository(CommunityDbContext db) : IConversationRepository
{
    public Task<Conversation?> FindAsync(Guid first, Guid second, CancellationToken cancellationToken) =>
        db.Conversations.FirstOrDefaultAsync(c => c.UserA == first && c.UserB == second, cancellationToken);

    public Task<ChatMessage?> FindMessageAsync(MessageId id, CancellationToken cancellationToken) =>
        db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ConversationRow>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Conversations.AsNoTracking()
            .Where(c => c.UserA == userId || c.UserB == userId)
            .OrderByDescending(c => c.LastMessageAt)
            .Select(c => new ConversationRow(
                c,
                db.Messages.Where(m => m.ConversationId == c.Id).OrderByDescending(m => m.SentAt).Select(m => m.Body).FirstOrDefault() ?? "",
                db.Messages.Count(m => m.ConversationId == c.Id && m.SenderId != userId && m.ReadAt == null)))
            .ToListAsync(cancellationToken);

    /// <summary>Returns the newest messages, oldest first.</summary>
    public async Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(ConversationId id, int take, CancellationToken cancellationToken)
    {
        var newest = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == id)
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        newest.Reverse();
        return newest;
    }

    public Task MarkReadAsync(ConversationId id, Guid readerId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Messages
            .Where(m => m.ConversationId == id && m.SenderId != readerId && m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, now), cancellationToken);

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Messages.CountAsync(
            m => m.ReadAt == null && m.SenderId != userId
                && db.Conversations.Any(c => c.Id == m.ConversationId && (c.UserA == userId || c.UserB == userId)),
            cancellationToken);

    public void Add(Conversation conversation) => db.Conversations.Add(conversation);

    public void Add(ChatMessage message) => db.Messages.Add(message);
}

internal sealed class MessageSentProcessor(IServiceScopeFactory scopes, ILogger<MessageSentProcessor> logger)
    : OutboxProcessor<CommunityDbContext>(scopes, logger)
{
    protected override string EventType => typeof(MessageSent).FullName!;

    protected override async Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var sent = JsonSerializer.Deserialize<MessageSent>(payload)!;
        var people = await services.GetRequiredService<IUserDirectory>().GetAsync([sent.SenderId], cancellationToken);
        var sender = people.GetValueOrDefault(sent.SenderId);

        // The note names the sender only. Private text stays out of notifications and email.
        await services.GetRequiredService<INotifier>().NotifyAsync(
            new NotificationRequest(sent.RecipientId, NotificationKind.NewMessage, $"message:{sent.MessageId}", null, sender?.NameAr, sender?.NameEn, $"/messages?with={sent.SenderId}"),
            cancellationToken);
    }
}

internal sealed class TopicRepliedProcessor(IServiceScopeFactory scopes, ILogger<TopicRepliedProcessor> logger)
    : OutboxProcessor<CommunityDbContext>(scopes, logger)
{
    protected override string EventType => typeof(TopicReplied).FullName!;

    protected override Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var replied = JsonSerializer.Deserialize<TopicReplied>(payload)!;

        return services.GetRequiredService<INotifier>().NotifyAsync(
            new NotificationRequest(replied.TopicAuthorId, NotificationKind.ForumReply, $"reply:{replied.PostId}", null, replied.TopicTitle, replied.TopicTitle, $"/forum/{replied.CohortId}/{replied.TopicId}"),
            cancellationToken);
    }
}

public static class CommunityModule
{
    public static IServiceCollection AddCommunityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CommunityDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("Lms"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", CommunityDbContext.SchemaName)));

        services.AddScoped<ICommunityUnitOfWork>(sp => sp.GetRequiredService<CommunityDbContext>());
        services.AddScoped<IForumRepository, ForumRepository>();
        services.AddScoped<IWikiRepository, WikiRepository>();
        services.AddScoped<WikiPages>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<ForumAccess>();
        services.AddScoped<MessagingPolicy>();
        services.AddHostedService<MessageSentProcessor>();
        services.AddHostedService<TopicRepliedProcessor>();
        services.AddScoped<Lms.BuildingBlocks.Application.IPersonalDataSource, CommunityPersonalData>();
        services.AddModuleApplication(typeof(IForumRepository).Assembly);
        return services;
    }
}
