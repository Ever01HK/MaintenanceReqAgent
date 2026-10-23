using System.Net;
using System.Net.Http.Json;
using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MaintenanceAgent.Api.Tests.Integration;

public class AgentHttpIntegrationTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public AgentHttpIntegrationTests(
        IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SameIdempotencyKey_WithDifferentMessage_ReturnsHttp409()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var property = new Property
        {
            Address = "101 Main St",
            TenantName = "Kim Tenant"
        };

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        var idempotencyKey = Guid.NewGuid().ToString();

        // Act 1: First request
        var firstResponse = await client.PostAsJsonAsync(
            "/api/agent/process",
            new
            {
                propertyId = property.Id,
                message = "Water leaking in the bathroom",
                idempotencyKey
            });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Act 2: Same key, different message
        var secondResponse = await client.PostAsJsonAsync(
            "/api/agent/process",
            new
            {
                propertyId = property.Id,
                message = "Heating system not working",
                idempotencyKey
            });

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            secondResponse.StatusCode);

        var approvals = await db.MaintenanceApprovals
            .Where(x => x.IdempotencyKey == idempotencyKey)
            .ToListAsync();

        Assert.Single(approvals);
    }

    [Fact]
    public async Task SameIdempotencyKey_WithSameMessage_ReusesExistingApproval()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var property = new Property
        {
            Address = "202 Test Street",
            TenantName = "Test Tenant"
        };

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        using var client = _factory.CreateClient();

        var idempotencyKey = Guid.NewGuid().ToString();

        var request = new
        {
            propertyId = property.Id,
            message = "Water leaking in the bathroom",
            idempotencyKey
        };

        // Act: First HTTP request
        var firstResponse = await client.PostAsJsonAsync(
            "/api/agent/process", request);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Act: Retry with exactly the same request
        var secondResponse = await client.PostAsJsonAsync(
            "/api/agent/process", request);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        // Assert: Compare HTTP response bodies
        using var firstJson = await firstResponse.Content
            .ReadFromJsonAsync<System.Text.Json.JsonDocument>();

        using var secondJson = await secondResponse.Content
            .ReadFromJsonAsync<System.Text.Json.JsonDocument>();

        Assert.NotNull(firstJson);
        Assert.NotNull(secondJson);

        var firstApprovalId = firstJson.RootElement
            .GetProperty("approvalId").GetInt32();

        var secondApprovalId = secondJson.RootElement
            .GetProperty("approvalId").GetInt32();

        Assert.Equal(firstApprovalId, secondApprovalId);

        Assert.False(firstJson.RootElement
            .GetProperty("reused").GetBoolean());

        Assert.True(secondJson.RootElement
            .GetProperty("reused").GetBoolean());

        // Assert: Only one approval exists in PostgreSQL
        var approvals = await db.MaintenanceApprovals
            .AsNoTracking()
            .Where(x => x.IdempotencyKey == idempotencyKey)
            .ToListAsync();

        Assert.Single(approvals);
        Assert.Equal(firstApprovalId, approvals[0].Id);
        Assert.Equal("Pending", approvals[0].Status);
    }
}