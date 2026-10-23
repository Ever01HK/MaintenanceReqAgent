using System.Net;
using System.Net.Http.Json;
using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MaintenanceAgent.Api.Tests.Integration;

public class GeminiFailureIntegrationTests
{
    private static async Task<int> CreatePropertyAsync(
        IntegrationTestFactory factory)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var property = new Property
        {
            Address = "707 Gemini Failure Street",
            TenantName = "Gemini Test Tenant"
        };

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        return property.Id;
    }

    [Fact]
    public async Task GeminiFirstCallFails_Returns500_WithoutApproval()
    {
        await using var factory =
            new GeminiFailureTestFactory(failFirstCall: true);

        await factory.InitializeAsync();

        var propertyId = await CreatePropertyAsync(factory);
        var key = Guid.NewGuid().ToString();

        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/agent/process",
            new
            {
                propertyId,
                message = "Water leaking",
                idempotencyKey = key
            });

        Assert.Equal(
            HttpStatusCode.InternalServerError,
            response.StatusCode);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var count = await db.MaintenanceApprovals
            .CountAsync(x => x.IdempotencyKey == key);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GeminiSecondCallFails_Returns200_WithPendingApproval()
    {
        await using var factory =
            new GeminiFailureTestFactory(failFirstCall: false);

        await factory.InitializeAsync();

        var propertyId = await CreatePropertyAsync(factory);
        var key = Guid.NewGuid().ToString();

        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/agent/process",
            new
            {
                propertyId,
                message = "Water leaking",
                idempotencyKey = key
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var approval = await db.MaintenanceApprovals
            .AsNoTracking()
            .SingleAsync(x => x.IdempotencyKey == key);

        Assert.Equal("Pending", approval.Status);
    }
}