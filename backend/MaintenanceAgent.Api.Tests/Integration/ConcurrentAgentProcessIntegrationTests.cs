using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MaintenanceAgent.Api.Tests.Integration;

public class ConcurrentAgentProcessIntegrationTests
    : IClassFixture<DeterministicGeminiTestFactory>
{
    private readonly DeterministicGeminiTestFactory _factory;

    public ConcurrentAgentProcessIntegrationTests(
        DeterministicGeminiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConcurrentDuplicateProcess_SamePayloadAndKey_CreatesSingleApproval()
    {
        // Arrange
        int propertyId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<MaintenanceDbContext>();

            var property = new Property
            {
                Address = "808 Concurrent Agent Street",
                TenantName = "Concurrent Agent Tenant"
            };

            db.Properties.Add(property);
            await db.SaveChangesAsync();

            propertyId = property.Id;
        }

        using var client1 = _factory.CreateClient();
        using var client2 = _factory.CreateClient();

        var idempotencyKey = Guid.NewGuid().ToString();
        var payload = new
        {
            propertyId,
            message = "Water leaking in the bathroom",
            idempotencyKey
        };

        // Act
        var request1 = client1.PostAsJsonAsync("/api/agent/process", payload);
        var request2 = client2.PostAsJsonAsync("/api/agent/process", payload);

        var responses = await Task.WhenAll(request1, request2);

        // Assert: both requests succeed
        Assert.All(
            responses,
            response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        using var body1 = await responses[0].Content.ReadFromJsonAsync<JsonDocument>();
        using var body2 = await responses[1].Content.ReadFromJsonAsync<JsonDocument>();

        Assert.NotNull(body1);
        Assert.NotNull(body2);

        var approvalId1 = body1.RootElement.GetProperty("approvalId").GetInt32();
        var approvalId2 = body2.RootElement.GetProperty("approvalId").GetInt32();

        Assert.Equal(approvalId1, approvalId2);

        var reused1 = body1.RootElement.GetProperty("reused").GetBoolean();
        var reused2 = body2.RootElement.GetProperty("reused").GetBoolean();

        Assert.Equal(1, new[] { reused1, reused2 }.Count(x => x));

        await using var verifyScope = _factory.Services.CreateAsyncScope();

        var verifyDb = verifyScope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var approvals = await verifyDb.MaintenanceApprovals
            .AsNoTracking()
            .Where(x => x.IdempotencyKey == idempotencyKey)
            .ToListAsync();

        Assert.Single(approvals);
        Assert.Equal(approvalId1, approvals[0].Id);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}
