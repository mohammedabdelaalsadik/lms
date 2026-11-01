using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lms.UnitTests;

public class AuditPipelineTests
{
    private sealed record SecretCommand(Guid TargetId, string Password, string NationalId, string Reason, int Hours) : ICommand<Unit>;

    private sealed record NoisyCommand(Guid TargetId) : ICommand<Unit>, INotAudited;

    private sealed record PlainQuery(Guid TargetId) : IQuery<Unit>;

    private sealed record SensitiveQuery(Guid TargetId) : IQuery<Unit>, IAuditedRequest;

    private sealed record SignInCommand : ICommand<SignedIn>;

    private sealed record SignedIn(Guid ActorId) : IAuditActor;

    private sealed class Handler<TRequest>(Error? error = null) : IRequestHandler<TRequest, Unit>
        where TRequest : IRequest<Unit>
    {
        public int Calls { get; private set; }

        public Task<Result<Unit>> Handle(TRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(error is null ? Result<Unit>.Success(default) : Result<Unit>.Failure(error));
        }
    }

    private sealed class SignInHandler(Guid actor) : IRequestHandler<SignInCommand, SignedIn>
    {
        public Task<Result<SignedIn>> Handle(SignInCommand request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<SignedIn>.Success(new SignedIn(actor)));
    }

    private sealed class FakeTrail(bool broken = false) : IAuditTrail
    {
        public List<AuditRecord> Records { get; } = [];

        public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
        {
            if (broken)
            {
                throw new InvalidOperationException("Audit store is down.");
            }

            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUser : ICurrentUser
    {
        public Guid? Id { get; } = Guid.NewGuid();
        public string? Role => "Admin";
        public string? IpAddress => "10.0.0.1";
    }

    private static IDispatcher Build(FakeTrail trail, Action<ServiceCollection> handlers, FakeUser? user = null)
    {
        var services = new ServiceCollection();
        services.AddLmsMessaging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAuditTrail>(trail);
        services.AddSingleton<ICurrentUser>(user ?? new FakeUser());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        handlers(services);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IDispatcher>();
    }

    [Fact]
    public async Task Command_is_recorded_with_who_what_and_where()
    {
        var trail = new FakeTrail();
        var user = new FakeUser();
        var dispatcher = Build(trail, s => s.AddSingleton<IRequestHandler<NoisyLike, Unit>>(new Handler<NoisyLike>()), user);

        await dispatcher.Send(new NoisyLike(Guid.NewGuid()), default);

        var record = Assert.Single(trail.Records);
        Assert.Equal(("NoisyLike", user.Id, "Admin", "10.0.0.1", true), (record.Action, record.ActorId, record.ActorRole, record.IpAddress, record.Succeeded));
    }

    private sealed record NoisyLike(Guid TargetId) : ICommand<Unit>;

    [Fact]
    public async Task Passwords_and_national_ids_never_reach_the_log()
    {
        var trail = new FakeTrail();
        var target = Guid.NewGuid();
        var dispatcher = Build(trail, s => s.AddSingleton<IRequestHandler<SecretCommand, Unit>>(new Handler<SecretCommand>()));

        await dispatcher.Send(new SecretCommand(target, "s3cret-pass", "1000000008", "مراجعة", 7), default);

        var details = Assert.Single(trail.Records).Details;
        Assert.DoesNotContain("s3cret-pass", details);
        Assert.DoesNotContain("1000000008", details);
        Assert.Contains(target.ToString(), details);
        Assert.Contains("\"Hours\":7", details);
        Assert.Contains("Reason", details);
    }

    [Fact]
    public async Task Failure_is_recorded_with_its_code()
    {
        var trail = new FakeTrail();
        var error = Error.Forbidden("x.denied", "No.");
        var dispatcher = Build(trail, s => s.AddSingleton<IRequestHandler<NoisyLike, Unit>>(new Handler<NoisyLike>(error)));

        await dispatcher.Send(new NoisyLike(Guid.NewGuid()), default);

        var record = Assert.Single(trail.Records);
        Assert.Equal((false, "x.denied"), (record.Succeeded, record.ErrorCode));
    }

    [Fact]
    public async Task Sign_in_names_the_user_the_session_cannot_yet()
    {
        var trail = new FakeTrail();
        var actor = Guid.NewGuid();
        var dispatcher = Build(trail, s => s.AddSingleton<IRequestHandler<SignInCommand, SignedIn>>(new SignInHandler(actor)));

        await dispatcher.Send(new SignInCommand(), default);

        Assert.Equal(actor, Assert.Single(trail.Records).ActorId);
    }

    [Fact]
    public async Task Routine_commands_and_plain_reads_are_skipped()
    {
        var trail = new FakeTrail();
        var dispatcher = Build(trail, s =>
        {
            s.AddSingleton<IRequestHandler<NoisyCommand, Unit>>(new Handler<NoisyCommand>());
            s.AddSingleton<IRequestHandler<PlainQuery, Unit>>(new Handler<PlainQuery>());
        });

        await dispatcher.Send(new NoisyCommand(Guid.NewGuid()), default);
        await dispatcher.Send(new PlainQuery(Guid.NewGuid()), default);

        Assert.Empty(trail.Records);
    }

    [Fact]
    public async Task Sensitive_read_is_recorded()
    {
        var trail = new FakeTrail();
        var dispatcher = Build(trail, s => s.AddSingleton<IRequestHandler<SensitiveQuery, Unit>>(new Handler<SensitiveQuery>()));

        await dispatcher.Send(new SensitiveQuery(Guid.NewGuid()), default);

        Assert.Equal("Sensitive", Assert.Single(trail.Records).Action);
    }

    [Fact]
    public async Task Sensitive_read_returns_nothing_when_no_trace_can_be_written()
    {
        var dispatcher = Build(new FakeTrail(broken: true), s => s.AddSingleton<IRequestHandler<SensitiveQuery, Unit>>(new Handler<SensitiveQuery>()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.Send(new SensitiveQuery(Guid.NewGuid()), default));
    }

    [Fact]
    public async Task Saved_command_still_reports_success_when_the_log_is_down()
    {
        var dispatcher = Build(new FakeTrail(broken: true), s => s.AddSingleton<IRequestHandler<NoisyLike, Unit>>(new Handler<NoisyLike>()));

        Assert.True((await dispatcher.Send(new NoisyLike(Guid.NewGuid()), default)).IsSuccess);
    }
}
