using Meridian.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Api;

public sealed class JwtBootstrapTests
{
    [Fact]
    public void EnsureConfiguredFor_Development_AllowsTheRepositoryDefaultKey()
    {
        var options = new JwtOptions { Key = JwtOptions.DevFallbackKey };

        options.EnsureConfiguredFor(isDevelopment: true);
    }

    [Fact]
    public void EnsureConfiguredFor_OutsideDevelopment_RejectsTheRepositoryDefaultKey()
    {
        var options = new JwtOptions { Key = JwtOptions.DevFallbackKey };

        var exception = Assert.Throws<InvalidOperationException>(
            () => options.EnsureConfiguredFor(isDevelopment: false));
        Assert.Contains("Jwt:Key", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureConfiguredFor_OutsideDevelopment_RejectsMissingKey(string key)
    {
        var options = new JwtOptions { Key = key };

        Assert.Throws<InvalidOperationException>(() => options.EnsureConfiguredFor(isDevelopment: false));
    }

    [Fact]
    public void EnsureConfiguredFor_OutsideDevelopment_AcceptsACustomKey()
    {
        var options = new JwtOptions { Key = "a-real-production-signing-key-with-enough-entropy" };

        options.EnsureConfiguredFor(isDevelopment: false);
    }

    [Theory]
    [InlineData("")]
    [InlineData(JwtOptions.DevFallbackKey)]
    public void Boot_OutsideDevelopment_WithoutAConfiguredKey_Fails(string key)
    {
        using var factory = new ProductionFactory(key);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Jwt:Key", Flatten(exception));
    }

    [Fact]
    public void Boot_OutsideDevelopment_WithAConfiguredKey_Succeeds()
    {
        using var factory = new ProductionFactory("a-real-production-signing-key-with-enough-entropy");

        var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
            messages.Add(current.Message);
        return string.Join(" | ", messages);
    }

    private sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        private readonly string _key;

        public ProductionFactory(string key) => _key = key;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("ConnectionStrings:Sqlite", "DataSource=:memory:");
            builder.UseSetting("Outbox:Enabled", "false");
            builder.UseSetting("Jwt:Key", _key);
        }
    }
}
