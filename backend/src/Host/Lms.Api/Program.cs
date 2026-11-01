using Lms.Api;
using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Infrastructure;
using Lms.BuildingBlocks.Presentation;
using Lms.Modules.Assessment.Infrastructure;
using Lms.Modules.Assessment.Presentation;
using Lms.Modules.Audit.Infrastructure;
using Lms.Modules.Audit.Presentation;
using Lms.Modules.Catalog.Infrastructure;
using Lms.Modules.Catalog.Presentation;
using Lms.Modules.Certification.Infrastructure;
using Lms.Modules.Certification.Presentation;
using Lms.Modules.Enrollment.Infrastructure;
using Lms.Modules.Enrollment.Presentation;
using Lms.Modules.Community.Infrastructure;
using Lms.Modules.Reports.Application;
using Lms.Modules.Reports.Presentation;
using Lms.Modules.Community.Presentation;
using Lms.Modules.Identity.Infrastructure;
using Lms.Modules.Identity.Presentation;
using Lms.Modules.Learning.Infrastructure;
using Lms.Modules.Learning.Presentation;
using Lms.Modules.Notifications.Infrastructure;
using Lms.Modules.Notifications.Presentation;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Scalar.AspNetCore;
using Serilog;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logger) => logger.ReadFrom.Configuration(context.Configuration));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddLmsMessaging();
builder.Services.AddLmsPresentation();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddCatalogModule(builder.Configuration);
builder.Services.AddEnrollmentModule(builder.Configuration);
builder.Services.AddLearningModule(builder.Configuration);
builder.Services.AddAssessmentModule(builder.Configuration);
builder.Services.AddCertificationModule(builder.Configuration);
builder.Services.AddScoped<Lms.Modules.Assessment.Application.IExamClient, Lms.Modules.Assessment.Presentation.HttpExamClient>();
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddCommunityModule(builder.Configuration);
builder.Services.AddReportsModule();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(AuthController).Assembly)
    .AddApplicationPart(typeof(CoursesController).Assembly)
    .AddApplicationPart(typeof(CohortsController).Assembly)
    .AddApplicationPart(typeof(LearningController).Assembly)
    .AddApplicationPart(typeof(AssessmentStaffController).Assembly)
    .AddApplicationPart(typeof(CertificatesController).Assembly)
    .AddApplicationPart(typeof(AuditController).Assembly)
    .AddApplicationPart(typeof(NotificationsController).Assembly)
    .AddApplicationPart(typeof(ForumsController).Assembly)
    .AddApplicationPart(typeof(ReportsController).Assembly)
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<IdentityDbContext>("database");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthController.AuthRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy(CertificatesController.VerifyRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, exception) =>
    context.RequestAborted.IsCancellationRequested ? Serilog.Events.LogEventLevel.Debug
    : exception is not null || context.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : Serilog.Events.LogEventLevel.Information);
app.Use((context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    return next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    await app.PrepareDevelopmentDatabaseAsync();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

CultureInfo[] cultures = [new("ar"), new("en")];
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("ar"),
    SupportedCultures = cultures,
    SupportedUICultures = cultures,
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<MfaSetupGate>();
app.UseMiddleware<PresenceMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();

public partial class Program;
