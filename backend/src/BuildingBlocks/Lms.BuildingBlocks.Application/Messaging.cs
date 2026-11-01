using FluentValidation;
using FluentValidation.Results;
using Lms.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Reflection;

namespace Lms.BuildingBlocks.Application;

public interface IRequest<TResponse>;

/// <summary>Changes state.</summary>
public interface ICommand<TResponse> : IRequest<TResponse>;

/// <summary>Reads state and has no side effects.</summary>
public interface IQuery<TResponse> : IRequest<TResponse>;

public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<Result<TResponse>> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface IDispatcher
{
    Task<Result<TResponse>> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken);
}

internal interface IPipeline<TResponse>
{
    Task<Result<TResponse>> Run(IRequest<TResponse> request, CancellationToken cancellationToken);
}

internal sealed class ValidationPipeline<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators,
    IRequestHandler<TRequest, TResponse> handler,
    AuditRecorder recorder) : IPipeline<TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<Result<TResponse>> Run(IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        var typed = (TRequest)request;
        var failures = new List<ValidationFailure>();

        foreach (var validator in validators)
        {
            var outcome = await validator.ValidateAsync(typed, cancellationToken);
            failures.AddRange(outcome.Errors);
        }

        if (failures.Count == 0)
        {
            var result = await handler.Handle(typed, cancellationToken);
            await recorder.RecordAsync(typed, result, cancellationToken);
            return result;
        }

        var grouped = failures
            .GroupBy(f => f.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());

        return new ValidationError(grouped);
    }
}

internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<(Type Request, Type Response), Type> PipelineTypes = new();

    public Task<Result<TResponse>> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        var pipelineType = PipelineTypes.GetOrAdd(
            (request.GetType(), typeof(TResponse)),
            key => typeof(ValidationPipeline<,>).MakeGenericType(key.Request, key.Response));

        var pipeline = (IPipeline<TResponse>)services.GetRequiredService(pipelineType);
        return pipeline.Run(request, cancellationToken);
    }
}

public static class ApplicationRegistration
{
    public static IServiceCollection AddLmsMessaging(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped(typeof(ValidationPipeline<,>));
        services.AddScoped<AuditRecorder>();
        return services;
    }

    /// <summary>Registers every request handler and validator in a module's Application assembly.</summary>
    public static IServiceCollection AddModuleApplication(this IServiceCollection services, Assembly assembly)
    {
        var registrations = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces(), (type, contract) => (type, contract))
            .Where(x => x.contract.IsGenericType
                && x.contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));

        foreach (var (type, contract) in registrations)
        {
            services.AddScoped(contract, type);
        }

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}
