using Keji.SmartQuery;

namespace Keji.SmartQuery.Tests;

public sealed class SmartQueryPublicContractTests
{
    [Fact]
    public void RequestExposesOnlyFrozenClientControlledFields()
    {
        var properties = typeof(KejiSmartQueryRequest).GetProperties()
            .Select(static property => property.Name).Order().ToArray();
        Assert.Equal(["DataSourceId", "Question", "RequestedLimit", "RunId"], properties);
    }

    [Fact]
    public void PublicServiceHasStreamAndAggregationEntrypointsOnly()
    {
        var methods = typeof(IKejiSmartQuery).GetMethods().Select(static method => method.Name).Order().ToArray();
        Assert.Equal(["RunAsync", "RunStreamAsync"], methods);
    }

    [Fact]
    public void SystemLimitIsFrozenAtTwoHundred()
    {
        Assert.Equal(200, KejiSmartQueryOptions.SystemMaxRows);
        Assert.DoesNotContain(typeof(KejiSmartQueryOptions).GetProperties(),
            static property => property.Name is "MaxRows" or "MaxAttempts");
    }
}
