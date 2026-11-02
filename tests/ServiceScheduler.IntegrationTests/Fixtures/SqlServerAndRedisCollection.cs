using Xunit;

namespace ServiceScheduler.IntegrationTests.Fixtures;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerAndRedisCollection :
    ICollectionFixture<SqlServerContainerFixture>,
    ICollectionFixture<RedisContainerFixture>
{
    public const string Name = "SqlServerAndRedis";
}
