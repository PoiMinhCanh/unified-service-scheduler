using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Features.Dealerships.Contracts;
using ServiceScheduler.Api.Infrastructure.Persistence;

namespace ServiceScheduler.Api.Features.Dealerships;

/// <summary>Returns seeded dealerships in the documented order.</summary>
[ApiController]
[Route("api/dealerships")]
public sealed class DealershipsController(SchedulerDbContext dbContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(DealershipResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DealershipResponse[]>> GetAsync(CancellationToken cancellationToken)
    {
        var dealerships = await dbContext.Dealerships
            .AsNoTracking()
            .OrderBy(dealership => dealership.Name)
            .ThenBy(dealership => dealership.Id)
            .Select(dealership => new DealershipResponse(
                dealership.Id,
                dealership.Name,
                dealership.TimeZoneId))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(dealerships);
    }
}
