using Microsoft.AspNetCore.Mvc;
using ServiceScheduler.Api.Features.ServiceCatalog.Contracts;
using ServiceScheduler.Api.Infrastructure.Errors;

namespace ServiceScheduler.Api.Features.ServiceCatalog;

/// <summary>Returns a dealership-scoped service catalog through the bounded cache-aside path.</summary>
[ApiController]
[Route("api/dealerships")]
public sealed class ServiceCatalogController(ServiceCatalogService catalogService) : ControllerBase
{
    [HttpGet("{dealershipId}/service-types")]
    [ProducesResponseType(typeof(ServiceTypeResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServiceTypeResponse[]>> GetAsync(
        Guid dealershipId,
        CancellationToken cancellationToken)
    {
        var result = await catalogService.GetAsync(dealershipId, cancellationToken).ConfigureAwait(false);

        return result.Status == ServiceCatalogStatus.NotFound
            ? ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                "The requested resource was not found.",
                "The dealership was not found.",
                ProblemCodes.NotFound)
            : Ok(result.Services);
    }
}
