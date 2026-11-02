using ServiceScheduler.Api.Infrastructure.Errors;
using Xunit;

namespace ServiceScheduler.UnitTests.Errors;

public sealed class SqlFailureClassifierTests
{
    [Theory]
    [InlineData(-2)]
    [InlineData(2)]
    [InlineData(53)]
    [InlineData(121)]
    [InlineData(233)]
    [InlineData(258)]
    [InlineData(1205)]
    [InlineData(1222)]
    [InlineData(4060)]
    [InlineData(40197)]
    [InlineData(40501)]
    [InlineData(40613)]
    [InlineData(49918)]
    [InlineData(49919)]
    [InlineData(49920)]
    [InlineData(10053)]
    [InlineData(10054)]
    [InlineData(10060)]
    [InlineData(10061)]
    [InlineData(10928)]
    [InlineData(10929)]
    [InlineData(11001)]
    public void ConnectivityTimeoutAndDeadlockNumbersAreClassified(int errorNumber)
    {
        Assert.True(SqlFailureClassifier.IsUnavailable(errorNumber));
    }

    [Theory]
    [InlineData(18456)]
    [InlineData(229)]
    [InlineData(50000)]
    [InlineData(208)]
    public void AuthenticationPermissionAndProgrammingNumbersAreNotClassified(int errorNumber)
    {
        Assert.False(SqlFailureClassifier.IsUnavailable(errorNumber));
    }
}
