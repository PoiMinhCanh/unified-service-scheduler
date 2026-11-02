using Microsoft.Data.SqlClient;

namespace ServiceScheduler.Api.Infrastructure.Errors;

/// <summary>
/// Recognizes a narrow set of SQL Server connectivity, timeout, and deadlock errors. Authentication,
/// permission, and programming failures are deliberately not treated as dependency outages.
/// </summary>
public static class SqlFailureClassifier
{
    private static readonly HashSet<int> UnavailableErrorNumbers =
    [
        -2, 2, 53, 121, 233, 258, 1205, 1222,
        4060, 40197, 40501, 40613,
        49918, 49919, 49920,
        10053, 10054, 10060, 10061,
        10928, 10929, 11001,
    ];

    public static bool IsUnavailable(int sqlErrorNumber) => UnavailableErrorNumbers.Contains(sqlErrorNumber);

    public static bool IsUnavailable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException && IsUnavailable(sqlException.Number))
            {
                return true;
            }
        }

        return false;
    }
}
