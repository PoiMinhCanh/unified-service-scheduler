using Microsoft.Extensions.Options;
using ServiceScheduler.Api.Infrastructure.Caching;
using Xunit;

namespace ServiceScheduler.UnitTests.Configuration;

public sealed class CacheOptionsValidatorTests
{
    private readonly CacheOptionsValidator _validator = new();

    [Fact]
    public void ValidOptionsSucceed()
    {
        var result = _validator.Validate(null, CreateValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void MissingConnectionStringFailsWithTheConfigurationKey()
    {
        var options = CreateValidOptions();
        options.ConnectionString = null;

        var result = _validator.Validate(null, options);

        AssertFailed(result, CacheOptions.ConnectionStringKey);
    }

    [Fact]
    public void MalformedConnectionStringFailsWithTheConfigurationKey()
    {
        var options = CreateValidOptions();
        options.ConnectionString = "127.0.0.1:not-a-port";

        var result = _validator.Validate(null, options);

        AssertFailed(result, CacheOptions.ConnectionStringKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveTtlFails(int seconds)
    {
        var options = CreateValidOptions();
        options.ServiceCatalog.AbsoluteTtl = TimeSpan.FromSeconds(seconds);

        var result = _validator.Validate(null, options);

        AssertFailed(result, "AbsoluteTtl");
    }

    [Fact]
    public void ExcessiveOperationTimeoutFails()
    {
        var options = CreateValidOptions();
        options.OperationTimeout = TimeSpan.FromSeconds(6);

        var result = _validator.Validate(null, options);

        AssertFailed(result, "OperationTimeout");
    }

    [Fact]
    public void NonPositiveConnectTimeoutFails()
    {
        var options = CreateValidOptions();
        options.ConnectTimeout = TimeSpan.Zero;

        var result = _validator.Validate(null, options);

        AssertFailed(result, "ConnectTimeout");
    }

    [Fact]
    public void NonPositiveCommandTimeoutFails()
    {
        var options = CreateValidOptions();
        options.CommandTimeout = TimeSpan.Zero;

        var result = _validator.Validate(null, options);

        AssertFailed(result, "CommandTimeout");
    }

    [Fact]
    public void NegativeConnectRetryFails()
    {
        var options = CreateValidOptions();
        options.ConnectRetry = -1;

        var result = _validator.Validate(null, options);

        AssertFailed(result, "ConnectRetry");
    }

    private static CacheOptions CreateValidOptions() => new()
    {
        ConnectionString = "127.0.0.1:6379",
        ServiceCatalog = new ServiceCatalogCacheOptions
        {
            AbsoluteTtl = TimeSpan.FromMinutes(10),
        },
        OperationTimeout = TimeSpan.FromMilliseconds(250),
        ConnectTimeout = TimeSpan.FromMilliseconds(500),
        CommandTimeout = TimeSpan.FromMilliseconds(500),
        ConnectRetry = 1,
    };

    private static void AssertFailed(ValidateOptionsResult result, string expectedFragment)
    {
        Assert.True(result.Failed);
        Assert.Contains(expectedFragment, result.FailureMessage, StringComparison.Ordinal);
    }
}
