using Microsoft.AspNetCore.Http;
using ServiceScheduler.Api.Infrastructure.Errors;
using Xunit;

namespace ServiceScheduler.UnitTests.Telemetry;

public sealed class ClientCancellationExceptionHandlerTests
{
    [Fact]
    public async Task AbortedCancellationIsHandledAsClientClosedRequest()
    {
        var context = new DefaultHttpContext();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        context.RequestAborted = cancellation.Token;
        var handler = new ClientCancellationExceptionHandler();

        var handled = await handler.TryHandleAsync(
            context,
            new OperationCanceledException(cancellation.Token),
            cancellation.Token);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, context.Response.StatusCode);
        Assert.Equal(ProblemCodes.ClientCancelled, context.Items[ProblemCodes.HttpContextItemKey]);
    }

    [Fact]
    public async Task NonAbortedCancellationIsNotHandled()
    {
        var context = new DefaultHttpContext();
        using var cancellation = new CancellationTokenSource();
        var handler = new ClientCancellationExceptionHandler();

        var handled = await handler.TryHandleAsync(
            context,
            new OperationCanceledException(cancellation.Token),
            cancellation.Token);

        Assert.False(handled);
    }

    [Fact]
    public async Task NonCancellationExceptionIsNotHandled()
    {
        var context = new DefaultHttpContext();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        context.RequestAborted = cancellation.Token;
        var handler = new ClientCancellationExceptionHandler();

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("test"),
            cancellation.Token);

        Assert.False(handled);
    }
}
