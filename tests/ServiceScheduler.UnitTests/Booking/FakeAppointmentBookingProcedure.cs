using ServiceScheduler.Api.Infrastructure.Persistence;

namespace ServiceScheduler.UnitTests.Booking;

public sealed class FakeAppointmentBookingProcedure : IAppointmentBookingProcedure
{
    private readonly Queue<BookingProcedureStatus> _statuses = new();

    public List<BookingProcedureRequest> Requests { get; } = [];

    public Func<BookingProcedureRequest, CancellationToken, Task<BookingProcedureResult>>? Handler { get; set; }

    public FakeAppointmentBookingProcedure(params BookingProcedureStatus[] statuses)
    {
        Enqueue(statuses);
    }

    public void Enqueue(params BookingProcedureStatus[] statuses)
    {
        foreach (var status in statuses)
        {
            _statuses.Enqueue(status);
        }
    }

    public Task<BookingProcedureResult> ExecuteAsync(
        BookingProcedureRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (Handler is not null)
        {
            return Handler(request, cancellationToken);
        }

        if (_statuses.Count == 0)
        {
            throw new InvalidOperationException("No scripted booking procedure result remains.");
        }

        var status = _statuses.Dequeue();
        return Task.FromResult(status == BookingProcedureStatus.Succeeded
            ? new BookingProcedureResult(
                BookingProcedureStatus.Succeeded,
                Guid.NewGuid(),
                request.StartUtc.AddMinutes(60),
                60,
                new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero))
            : new BookingProcedureResult(status));
    }
}
