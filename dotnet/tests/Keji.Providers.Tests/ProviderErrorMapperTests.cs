using System.Net;
using Keji.Providers;

namespace Keji.Providers.Tests;

public class ProviderErrorMapperTests
{
    [Fact]
    public void Map_401_ReturnsAuthFailed()
    {
        var (code, _) = ProviderErrorMapper.Map(HttpStatusCode.Unauthorized, null);
        Assert.Equal("AUTH_FAILED", code);
    }

    [Fact]
    public void Map_403_ReturnsAuthFailed()
    {
        var (code, _) = ProviderErrorMapper.Map(HttpStatusCode.Forbidden, null);
        Assert.Equal("AUTH_FAILED", code);
    }

    [Fact]
    public void Map_404_ReturnsEndpointNotFound()
    {
        var (code, _) = ProviderErrorMapper.Map(HttpStatusCode.NotFound, null);
        Assert.Equal("ENDPOINT_NOT_FOUND", code);
    }

    [Fact]
    public void Map_429_ReturnsRateLimited()
    {
        var (code, _) = ProviderErrorMapper.Map(HttpStatusCode.TooManyRequests, null);
        Assert.Equal("RATE_LIMITED", code);
    }

    [Fact]
    public void Map_503_ReturnsServiceUnavailable()
    {
        var (code, _) = ProviderErrorMapper.Map(HttpStatusCode.ServiceUnavailable, null);
        Assert.Equal("SERVICE_UNAVAILABLE", code);
    }

    [Fact]
    public void Map_502_ReturnsGatewayError()
    {
        var (code, _) = ProviderErrorMapper.Map(HttpStatusCode.BadGateway, null);
        Assert.Equal("GATEWAY_ERROR", code);
    }

    [Fact]
    public void Map_504_ReturnsGatewayTimeout()
    {
        var (code, _) = ProviderErrorMapper.Map(HttpStatusCode.GatewayTimeout, null);
        Assert.Equal("GATEWAY_TIMEOUT", code);
    }

    [Fact]
    public void Map_500_ReturnsServerError()
    {
        var (code, _) = ProviderErrorMapper.Map(HttpStatusCode.InternalServerError, null);
        Assert.Equal("SERVER_ERROR", code);
    }

    [Fact]
    public void Map_400_ReturnsRequestError()
    {
        var (code, _) = ProviderErrorMapper.Map(HttpStatusCode.BadRequest, null);
        Assert.Equal("REQUEST_ERROR", code);
    }

    [Fact]
    public void Map_413_ReturnsRequestTooLarge()
    {
        var (code, _) = ProviderErrorMapper.Map(HttpStatusCode.RequestEntityTooLarge, null);
        Assert.Equal("REQUEST_TOO_LARGE", code);
    }

    [Fact]
    public void Map_Unknown_ReturnsRequestError()
    {
        var (code, _) = ProviderErrorMapper.Map((HttpStatusCode)418, null);
        Assert.Equal("REQUEST_ERROR", code);
    }

    [Fact]
    public void MapFromException_OperationCanceled_ReturnsCancelled()
    {
        var (code, _) = ProviderErrorMapper.MapFromException(new OperationCanceledException());
        Assert.Equal("CANCELLED", code);
    }

    [Fact]
    public void MapFromException_TaskCanceled_ReturnsTimeout()
    {
        var (code, _) = ProviderErrorMapper.MapFromException(new TaskCanceledException());
        Assert.Equal("TIMEOUT", code);
    }

    [Fact]
    public void MapFromException_HttpRequestException_DelegatesToMap()
    {
        var ex = new HttpRequestException("msg", null, HttpStatusCode.ServiceUnavailable);
        var (code, _) = ProviderErrorMapper.MapFromException(ex);
        Assert.Equal("SERVICE_UNAVAILABLE", code);
    }

    [Fact]
    public void MapFromException_GenericException_ReturnsConnectionError()
    {
        var (code, _) = ProviderErrorMapper.MapFromException(new InvalidOperationException());
        Assert.Equal("CONNECTION_ERROR", code);
    }

    [Fact]
    public void MapFromException_NullStatusCodeHttpRequest_ReturnsConnectionError()
    {
        var ex = new HttpRequestException("no status");
        var (code, _) = ProviderErrorMapper.MapFromException(ex);
        Assert.Equal("CONNECTION_ERROR", code);
    }
}
