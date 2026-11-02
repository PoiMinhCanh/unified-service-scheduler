using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using ServiceScheduler.Api.Infrastructure.Errors;
using ServiceScheduler.Api.Infrastructure.Telemetry;
using Xunit;

namespace ServiceScheduler.UnitTests.Telemetry;

public sealed class RequestLoggingMiddlewareTests
{
    private const string RouteTemplate = "api/dealerships/{dealershipId}/appointments";

    [Fact]
    public async Task LogsOneSanitizedCompletionEventForARouteTemplate()
    {
        var logger = new RecordingLogger<RequestLoggingMiddleware>();
        var context = CreateContext(RouteTemplate, "/api/dealerships/11111111-1111-1111-1111-111111111111/appointments");
        context.Request.Method = "POST";
        context.Request.QueryString = new QueryString("?vin=SYNTHETICVIN123");
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Items[ProblemCodes.HttpContextItemKey] = ProblemCodes.CapacityUnavailable;
        var middleware = new RequestLoggingMiddleware(static _ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        var record = Assert.Single(logger.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("RequestCompleted", record.Properties["EventName"]);
        Assert.Equal("POST", record.Properties["RequestMethod"]);
        Assert.Equal(RouteTemplate, record.Properties["RouteTemplate"]);
        Assert.Equal(StatusCodes.Status409Conflict, record.Properties["StatusCode"]);
        Assert.Equal("4xx", record.Properties["StatusClass"]);
        Assert.Equal(ProblemCodes.CapacityUnavailable, record.Properties["Outcome"]);
        Assert.IsType<long>(record.Properties["ElapsedMilliseconds"]);
        Assert.DoesNotContain("SYNTHETICVIN123", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("11111111-1111-1111-1111-111111111111", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReconstructsACombinedRoutePatternWithoutRawText()
    {
        var combined = RoutePatternFactory.Combine(
            RoutePatternFactory.Parse("api/dealerships"),
            RoutePatternFactory.Parse("{dealershipId}/service-types"));
        var logger = new RecordingLogger<RequestLoggingMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/dealerships/11111111-1111-1111-1111-111111111111/service-types";
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.SetEndpoint(new RouteEndpoint(
            static _ => Task.CompletedTask,
            combined,
            order: 0,
            EndpointMetadataCollection.Empty,
            displayName: "combined"));
        var middleware = new RequestLoggingMiddleware(static _ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        var record = Assert.Single(logger.Records);
        Assert.Equal("api/dealerships/{dealershipId}/service-types", record.Properties["RouteTemplate"]);
    }

    [Fact]
    public async Task HealthRequestsAreNotLogged()
    {
        var logger = new RecordingLogger<RequestLoggingMiddleware>();
        var context = CreateContext(routeTemplate: null, "/health/ready");
        context.Request.Method = "GET";
        var nextCalled = false;
        var middleware = new RequestLoggingMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Empty(logger.Records);
    }

    [Fact]
    public async Task UnmatchedRouteUsesTheBoundedPlaceholder()
    {
        var logger = new RecordingLogger<RequestLoggingMiddleware>();
        var context = CreateContext(routeTemplate: null, "/api/unknown-route");
        context.Request.Method = "GET";
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        var middleware = new RequestLoggingMiddleware(static _ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        var record = Assert.Single(logger.Records);
        Assert.Equal("unmatched", record.Properties["RouteTemplate"]);
        Assert.Equal(ProblemCodes.NotFound, record.Properties["Outcome"]);
        Assert.Equal("4xx", record.Properties["StatusClass"]);
    }

    [Fact]
    public async Task UnexpectedExceptionIsLoggedOnceAndRethrown()
    {
        var logger = new RecordingLogger<RequestLoggingMiddleware>();
        var context = CreateContext(RouteTemplate, "/api/dealerships/11111111-1111-1111-1111-111111111111/appointments");
        context.Request.Method = "POST";
        var exception = new InvalidOperationException("unexpected");
        var middleware = new RequestLoggingMiddleware(_ => throw exception, logger);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context));

        Assert.Same(exception, thrown);
        var record = Assert.Single(logger.Records);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Same(exception, record.Exception);
        Assert.Equal(ProblemCodes.InternalError, record.Properties["Outcome"]);
        Assert.Equal("5xx", record.Properties["StatusClass"]);
    }

    [Fact]
    public async Task SuccessfulRequestOutcomeFallsBackToNone()
    {
        var logger = new RecordingLogger<RequestLoggingMiddleware>();
        var context = CreateContext(RouteTemplate, "/api/dealerships/11111111-1111-1111-1111-111111111111/appointments");
        context.Request.Method = "GET";
        context.Response.StatusCode = StatusCodes.Status201Created;
        var middleware = new RequestLoggingMiddleware(static _ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        var record = Assert.Single(logger.Records);
        Assert.Equal("none", record.Properties["Outcome"]);
        Assert.Equal("2xx", record.Properties["StatusClass"]);
        Assert.Equal(LogLevel.Information, record.Level);
    }

    [Fact]
    public async Task ClientCancellationIsLoggedAsInformationWithoutErrorOutcome()
    {
        var logger = new RecordingLogger<RequestLoggingMiddleware>();
        using var metrics = new MetricTestListener();
        var context = CreateContext(RouteTemplate, "/api/dealerships/11111111-1111-1111-1111-111111111111/appointments");
        context.Request.Method = "POST";
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        context.RequestAborted = cancellation.Token;
        var exception = new OperationCanceledException(cancellation.Token);
        var middleware = new RequestLoggingMiddleware(_ => throw exception, logger);

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            () => middleware.InvokeAsync(context));

        Assert.Same(exception, thrown);
        var record = Assert.Single(logger.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Null(record.Exception);
        Assert.Equal(ProblemCodes.ClientCancelled, record.Properties["Outcome"]);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, record.Properties["StatusCode"]);
        Assert.Contains(
            metrics.Measurements,
            measurement => measurement.Instrument == ServiceSchedulerTelemetry.HttpRequestsInstrumentName
                           && Equals(measurement.Tags["outcome"], ProblemCodes.ClientCancelled)
                           && Equals(measurement.Tags["status_class"], "4xx"));
    }

    private static DefaultHttpContext CreateContext(string? routeTemplate, string rawPath)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = rawPath;
        if (routeTemplate is not null)
        {
            var endpoint = new RouteEndpoint(
                static _ => Task.CompletedTask,
                RoutePatternFactory.Parse(routeTemplate),
                order: 0,
                EndpointMetadataCollection.Empty,
                displayName: "test");
            context.SetEndpoint(endpoint);
        }

        return context;
    }
}
