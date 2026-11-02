using Xunit;

namespace ServiceScheduler.IntegrationTests.Fixtures;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>
{
    public const string Name = "SqlServer";
}
