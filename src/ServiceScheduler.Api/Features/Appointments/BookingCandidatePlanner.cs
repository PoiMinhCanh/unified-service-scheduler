namespace ServiceScheduler.Api.Features.Appointments;

public sealed class BookingCandidatePlanner
{
    public IReadOnlyList<BookingCandidatePair> BuildPairs(
        IReadOnlyList<Guid> technicianIds,
        IReadOnlyList<Guid> bayIds,
        int maxCandidateAttempts)
    {
        ArgumentNullException.ThrowIfNull(technicianIds);
        ArgumentNullException.ThrowIfNull(bayIds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCandidateAttempts);

        var technicians = technicianIds.Where(static id => id != Guid.Empty).Distinct().ToArray();
        var bays = bayIds.Where(static id => id != Guid.Empty).Distinct().ToArray();
        var pairs = new List<BookingCandidatePair>(maxCandidateAttempts);

        foreach (var technicianId in technicians)
        {
            foreach (var bayId in bays)
            {
                if (pairs.Count == maxCandidateAttempts)
                {
                    return pairs;
                }

                pairs.Add(new BookingCandidatePair(technicianId, bayId));
            }
        }

        return pairs;
    }
}

public sealed record BookingCandidatePair(Guid TechnicianId, Guid ServiceBayId);
