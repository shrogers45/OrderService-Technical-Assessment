using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OrderService.Tests;

public class OrderServiceEndpointTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public OrderServiceEndpointTests(
        WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result =
            await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.NotNull(result);
        Assert.Equal("healthy", result.Status);
    }

    [Fact]
    public async Task VersionEndpoint_DefaultsToDev()
    {
        var originalVersion =
            Environment.GetEnvironmentVariable("APP_VERSION");

        try
        {
            Environment.SetEnvironmentVariable("APP_VERSION", null);

            var response = await _client.GetAsync("/version");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result =
                await response.Content.ReadFromJsonAsync<VersionResponse>();

            Assert.NotNull(result);
            Assert.Equal("dev", result.Version);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "APP_VERSION",
                originalVersion);
        }
    }

    private sealed record HealthResponse(string Status);

    private sealed record VersionResponse(string Version);
}