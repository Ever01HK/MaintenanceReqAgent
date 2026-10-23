using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace MaintenanceAgent.Api.Tests.Integration;

public class IntegrationTestFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("maintenance_test_db")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        await db.Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(
    IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting(
            "ConnectionStrings:MaintenanceDb",
            _postgres.GetConnectionString());
        builder.UseSetting("Gemini:ApiKey", "test-api-key");
        builder.UseSetting("Gemini:Model", "gemini-test-model");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<MaintenanceDbContext>>();

            services.AddDbContext<MaintenanceDbContext>(options =>
                options.UseNpgsql(
                    _postgres.GetConnectionString()));

            services.AddHttpClient<GeminiAgentService>()
                .ConfigurePrimaryHttpMessageHandler(
                    () => new FakeGeminiHandler());
        });
    }
    
    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}