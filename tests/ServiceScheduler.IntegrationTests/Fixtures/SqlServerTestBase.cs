using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Fixtures;

/// <summary>
/// Gives a test class one initialized database for all of its test methods. Mutating tests must
/// roll back their own probes or remove their own rows; the shared database is dropped by the
/// collection fixture after the run.
/// </summary>
public abstract class SqlServerTestBase : IAsyncLifetime
{
    protected SqlServerTestBase(SqlServerContainerFixture fixture)
    {
        Fixture = fixture;
    }

    protected SqlServerContainerFixture Fixture { get; }

    protected SqlTestDatabase Database { get; private set; } = null!;

    protected string SetupConnectionString => Database.SetupConnectionString;

    protected string RuntimeConnectionString => Database.RuntimeConnectionString;

    public async ValueTask InitializeAsync()
    {
        Database = await Fixture.GetDatabaseAsync(GetType()).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected SchedulerApiFactory CreateFactory(
        TimeProvider? clock = null,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Runtime"] = RuntimeConnectionString,
        };

        if (overrides is not null)
        {
            foreach (var setting in overrides)
            {
                settings[setting.Key] = setting.Value;
            }
        }

        return new SchedulerApiFactory(settings)
        {
            TimeProvider = clock,
            ConfigureTestServices = configureServices,
        };
    }

    protected SchedulingTestData TestData => new(SetupConnectionString);
}
