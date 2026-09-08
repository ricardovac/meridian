using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Meridian.Tests.Api;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Health_WithReachableDatabase_Returns200_AndReportsConnected()
    {
        using var factory = new MeridianApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.ReadAsAsync<HealthPayload>();
        Assert.Equal("healthy", payload!.Status);
        Assert.Equal("connected", payload.Database);
    }

    [Fact]
    public async Task Health_WithUnreachableDatabase_Returns503_AndReportsUnavailable()
    {
        using var factory = new UnreachableDatabaseFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var payload = await response.ReadAsAsync<HealthPayload>();
        Assert.Equal("degraded", payload!.Status);
        Assert.Equal("unavailable", payload.Database);
    }

    private sealed record HealthPayload(string Status, string Database);

    private sealed class UnreachableDatabaseFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:Provider", "Postgres");
            builder.UseSetting(
                "ConnectionStrings:Default",
                "Host=127.0.0.1;Port=1;Database=meridian;Username=meridian;Password=meridian;Timeout=1");
            builder.UseSetting("Outbox:Enabled", "false");
        }
    }
}
