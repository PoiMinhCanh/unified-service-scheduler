using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ServiceScheduler.Api.Features.Appointments;
using ServiceScheduler.Api.Features.Availability;
using ServiceScheduler.Api.Features.ServiceCatalog;
using ServiceScheduler.Api.Infrastructure.Caching;
using ServiceScheduler.Api.Infrastructure.Configuration;
using ServiceScheduler.Api.Infrastructure.Errors;
using ServiceScheduler.Api.Infrastructure.Health;
using ServiceScheduler.Api.Infrastructure.OpenApi;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Setup;
using ServiceScheduler.Api.Infrastructure.Telemetry;

if (args.Contains("--db-setup", StringComparer.Ordinal))
{
    return await DatabaseSetupCommand.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);

// The local demo returns Problem Details for failures, not developer exception pages.
builder.WebHost.UseSetting(WebHostDefaults.DetailedErrorsKey, "false");

builder.Services.AddOptions<TelemetryOptions>()
    .Bind(builder.Configuration.GetSection(TelemetryOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<TelemetryOptions>, TelemetryOptionsValidator>();

builder.AddSchedulerLogging();
builder.AddSchedulerTelemetry();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.Configure<ProblemDetailsOptions>(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var problemDetails = context.ProblemDetails;
        var code = ProblemCodes.ForStatus(problemDetails.Status ?? context.HttpContext.Response.StatusCode);
        if (code is not null)
        {
            if (!problemDetails.Extensions.ContainsKey("code"))
            {
                problemDetails.Extensions["code"] = code;
            }

            context.HttpContext.Items[ProblemCodes.HttpContextItemKey] = code;
        }

        // The framework's Problem Details defaults may have inserted a traceparent-style value;
        // the API contract is the W3C trace ID (or a non-empty request-identifier fallback).
        problemDetails.Extensions["traceId"] = ProblemTraceId.Resolve(context.HttpContext.TraceIdentifier);
    };
});
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problemDetails = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "The request is invalid.",
        };
        problemDetails.Extensions["code"] = ProblemCodes.ValidationError;
        problemDetails.Extensions["traceId"] = ProblemTraceId.Resolve(context.HttpContext.TraceIdentifier);
        context.HttpContext.Items[ProblemCodes.HttpContextItemKey] = ProblemCodes.ValidationError;

        return new BadRequestObjectResult(problemDetails)
        {
            ContentTypes = { "application/problem+json" },
        };
    };
});
builder.Services.AddExceptionHandler<ClientCancellationExceptionHandler>();
builder.Services.AddExceptionHandler<SqlDependencyExceptionHandler>();

builder.Services.AddOpenApi(options =>
{
    options.AddOperationTransformer<ApiOperationTransformer>();
    options.AddSchemaTransformer<ProblemDetailsSchemaTransformer>();
});

builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();

builder.Services.AddOptions<CacheOptions>()
    .Bind(builder.Configuration.GetSection(CacheOptions.SectionName))
    .Configure(options =>
        options.ConnectionString = builder.Configuration.GetConnectionString(CacheOptions.ConnectionStringName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<CacheOptions>, CacheOptionsValidator>();

builder.Services.AddOptions<BookingOptions>()
    .Bind(builder.Configuration.GetSection(BookingOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<BookingOptions>, BookingOptionsValidator>();

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

builder.Services.AddDbContext<SchedulerDbContext>((serviceProvider, options) =>
{
    var databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    options.UseSqlServer(databaseOptions.Runtime);
});

builder.Services.AddStackExchangeRedisCache(_ => { });
builder.Services.AddSingleton<IConfigureOptions<RedisCacheOptions>, RedisCacheOptionsSetup>();
builder.Services.AddSingleton<BoundedCache>();

builder.Services.AddScoped<ServiceCatalogService>();
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<IAppointmentBookingProcedure, SqlAppointmentBookingProcedure>();
builder.Services.AddSingleton<BookingCandidatePlanner>();
builder.Services.AddScoped<BookingAllocator>();
builder.Services.AddScoped<AppointmentService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<SchedulerDbContext>("database", tags: ["ready"])
    .AddCheck<CacheHealthCheck>(CacheHealthCheck.Name, tags: ["ready"]);

builder.Services.Configure<HealthCheckServiceOptions>(options =>
{
    foreach (var registration in options.Registrations)
    {
        registration.Timeout = registration.Name == "database"
            ? TimeSpan.FromSeconds(5)
            : TimeSpan.FromSeconds(2);
    }
});

var app = builder.Build();

// Select the endpoint before request telemetry so the completion event records the route
// template even when the request later fails inside a controller or dependency.
app.UseRouting();
// The completion middleware observes the final status after Problem Details handling.
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "ServiceScheduler v1");
    });
    app.MapOpenApi();
}

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Any(tag => string.Equals(tag, "ready", StringComparison.Ordinal)),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

app.Run();
return 0;

public partial class Program;
