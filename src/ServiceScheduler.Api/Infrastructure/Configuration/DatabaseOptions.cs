namespace ServiceScheduler.Api.Infrastructure.Configuration;

/// <summary>
/// Binds the runtime SQL Server connection used by the API. The setup connection is deliberately
/// not part of this options type and is never loaded by API mode.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    public string? Runtime { get; set; }
}
