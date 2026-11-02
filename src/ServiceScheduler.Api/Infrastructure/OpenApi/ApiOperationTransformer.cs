using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ServiceScheduler.Api.Infrastructure.OpenApi;

/// <summary>
/// Documents ordering, explicit-offset input, availability outcomes, and Problem Details statuses
/// without embedding expiring dates or seeded identifiers as the only examples.
/// </summary>
public sealed class ApiOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var path = context.Description.RelativePath?.TrimStart('/');

        if (string.Equals(path, "api/dealerships", StringComparison.OrdinalIgnoreCase))
        {
            operation.Summary = "List dealerships";
            operation.Description =
                "Returns seeded dealerships with their IANA time-zone identifiers, ordered by name ascending and then by ID.";
            DescribeResponses(operation, notFound: false);
        }
        else if (string.Equals(path, "api/dealerships/{dealershipId}/service-types", StringComparison.OrdinalIgnoreCase))
        {
            operation.Summary = "List dealership service types";
            operation.Description =
                "Returns dealership-scoped service types ordered by name ascending and then by ID. " +
                "An existing dealership with no services returns an empty array. Results use a bounded Redis cache-aside path with a 10-minute absolute TTL; availability always reads duration from SQL Server.";
            SetParameterDescription(operation, "dealershipId", "Dealership identifier (GUID). A malformed value returns 400 validation_error; an unknown dealership returns 404 not_found.");
            DescribeResponses(operation, notFound: true);
        }
        else if (string.Equals(path, "api/dealerships/{dealershipId}/availability", StringComparison.OrdinalIgnoreCase))
        {
            operation.Summary = "Evaluate appointment availability";
            operation.Description =
                "Evaluates the requested start and the authoritative SQL Server service duration. " +
                "available is true only when an active, qualified technician and an active bay cover the full interval within one opening interval and one technician shift, with no overlapping confirmed appointment. " +
                "The response uses the same six fields for available true and false. The request is advisory and creates no reservation.";
            SetParameterDescription(operation, "dealershipId", "Dealership identifier (GUID). An unknown dealership returns 404 not_found.");
            SetParameterDescription(operation, "serviceTypeId", "Service type identifier (GUID) scoped to the dealership. Missing or malformed values return 400 validation_error; an unknown or cross-dealership service returns 404 not_found.");
            SetParameterDescription(operation, "startsAt", "Requested UTC start with Z or an explicit numeric offset. Offset-free, date-only, past, or non-future values return 400 validation_error. Encode a positive offset as %2B in the query string; the executable demo script computes a valid future start.");
            SetParameterRequired(operation, "dealershipId");
            SetParameterRequired(operation, "serviceTypeId");
            SetParameterRequired(operation, "startsAt");
            DescribeResponses(operation, notFound: true);
        }
        else if (string.Equals(path, "api/dealerships/{dealershipId}/appointments", StringComparison.OrdinalIgnoreCase))
        {
            operation.Summary = "Create an appointment";
            operation.Description =
                "Validates the customer, vehicle ownership, service scope, explicit-offset start, and full opening/shift coverage. " +
                "The server selects a qualified technician and an active bay, snapshots the SQL Server service duration, and persists one Confirmed appointment through the restricted booking procedure. " +
                "A successful response includes a followable Location for retrieval. A valid request that cannot reserve an eligible pair within the bounded allocation policy returns 409 capacity_unavailable.";
            SetParameterDescription(operation, "dealershipId", "Dealership identifier (GUID). A malformed value returns 400 validation_error; an unknown dealership returns 404 not_found.");
            SetParameterRequired(operation, "dealershipId");
            if (operation.RequestBody is OpenApiRequestBody requestBody)
            {
                requestBody.Required = true;
                requestBody.Description =
                    "Required booking details. Replace the example identifiers with existing records and startsAt with a future timestamp containing Z or an explicit offset. The vehicle must belong to the customer and the service type to this dealership.";
                if (requestBody.Content is not null
                    && requestBody.Content.TryGetValue("application/json", out var json))
                {
                    json.Example = new JsonObject
                    {
                        ["customerId"] = "c1000000-0000-0000-0000-000000000001",
                        ["vehicleId"] = "b1000000-0000-0000-0000-000000000001",
                        ["serviceTypeId"] = "51000000-0000-0000-0000-000000000001",
                        ["startsAt"] = "2026-11-03T10:00:00+07:00",
                    };
                }
            }
            Describe(operation, "201", "Appointment confirmed. Location points to GET /api/dealerships/{dealershipId}/appointments/{appointmentId}; the body contains all persisted associations, UTC start/end, duration snapshot, Confirmed status, and creation time.");
            Describe(operation, "400", "Malformed or missing input, malformed UUIDs, offset-free/unparseable/past starts, interval overflow, or ownership mismatch. Problem Details include code=validation_error.");
            Describe(operation, "404", "Unknown dealership, customer, vehicle, or service type, or a service type from another dealership. Problem Details include code=not_found.");
            Describe(operation, "409", "Valid request with no eligible technician/bay pair under the documented bounded allocation policy, including insufficient opening/shift coverage. Problem Details include code=capacity_unavailable; this is not a validation or dependency failure.");
            Describe(operation, "503", "Classified SQL Server dependency, lock, or timeout failure. Problem Details include code=dependency_unavailable; a write with an unknown commit outcome is not replayed.");
            Describe(operation, "500", "Sanitized unexpected failure, including an invalid persisted SQL time-zone mapping. Problem Details include code=internal_error and no stack trace or credentials.");
        }
        else if (string.Equals(path, "api/dealerships/{dealershipId}/appointments/{appointmentId}", StringComparison.OrdinalIgnoreCase))
        {
            operation.Summary = "Retrieve an appointment";
            operation.Description =
                "Returns the persisted appointment for the requested dealership using the stored start, end, duration snapshot, status, and creation time. " +
                "The appointment is not recomputed from the current service catalog. Missing or other-dealership appointments return 404 not_found. Retrieval has no capacity-conflict outcome.";
            SetParameterDescription(operation, "dealershipId", "Dealership identifier (GUID). A malformed value returns 400 validation_error.");
            SetParameterDescription(operation, "appointmentId", "Appointment identifier (GUID). An appointment from another dealership returns 404 not_found.");
            SetParameterRequired(operation, "dealershipId");
            SetParameterRequired(operation, "appointmentId");
            Describe(operation, "200", "Persisted appointment with all associations, UTC timestamps with Z, stored duration/end snapshot, Confirmed status, and creation time.");
            Describe(operation, "400", "Malformed dealershipId or appointmentId route GUID. Problem Details include code=validation_error.");
            Describe(operation, "404", "The appointment does not exist or belongs to another dealership. Problem Details include code=not_found.");
            Describe(operation, "503", "Classified SQL Server dependency failure. Problem Details include code=dependency_unavailable.");
            Describe(operation, "500", "Sanitized unexpected failure. Problem Details include code=internal_error and no stack trace or credentials.");
        }

        return Task.CompletedTask;
    }

    private static void SetParameterDescription(OpenApiOperation operation, string name, string description)
    {
        var parameter = operation.Parameters?
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (parameter is not null)
        {
            parameter.Description = description;
        }
    }

    private static void SetParameterRequired(OpenApiOperation operation, string name)
    {
        var parameter = operation.Parameters?
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (parameter is OpenApiParameter concreteParameter)
        {
            // IOpenApiParameter exposes Required as read-only; the concrete document model is mutable.
            concreteParameter.Required = true;
        }
    }

    private static void DescribeResponses(OpenApiOperation operation, bool notFound)
    {
        Describe(operation, "200", "Successful response.");
        Describe(operation, "400", "Invalid input: malformed or missing identifiers, offset-free/unparseable timestamp, non-future start, or computed end overflow. Problem Details include code=validation_error.");
        if (notFound)
        {
            Describe(operation, "404", "Unknown dealership, unknown service type, or a service type from another dealership. Problem Details include code=not_found.");
        }

        Describe(operation, "503", "Classified SQL Server unavailability. Problem Details include code=dependency_unavailable; discovery, availability, and a catalog miss fail instead of returning false availability.");
        Describe(operation, "500", "Sanitized unexpected failure. Problem Details include code=internal_error and no stack trace, credentials, or connection string.");
    }

    private static void Describe(OpenApiOperation operation, string statusCode, string description)
    {
        if (operation.Responses is not null && operation.Responses.TryGetValue(statusCode, out var response))
        {
            response.Description = description;
        }
    }
}
