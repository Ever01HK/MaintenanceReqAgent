using System.Net;
using System.Net.Http.Json;
using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MaintenanceAgent.Api.Tests.Integration;

public class ConcurrentApprovalIntegrationTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public ConcurrentApprovalIntegrationTests(
        IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConcurrentApprove_OnlyOneMaintenanceRequestIsCreated()
    {
        // Arrange: Create test data in PostgreSQL
        int approvalId;
        int propertyId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<MaintenanceDbContext>();

            var property = new Property
            {
                Address = "303 Concurrent Test Street",
                TenantName = "Concurrent Test Tenant"
            };

            db.Properties.Add(property);
            await db.SaveChangesAsync();

            propertyId = property.Id;

            var approval = new MaintenanceApproval
            {
                PropertyId = propertyId,
                Description = "Water leaking in the bathroom",
                Category = "Plumbing",
                Priority = "High",
                Status = "Pending",
                IdempotencyKey = Guid.NewGuid().ToString(),
                RequestHash = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow
            };

            db.MaintenanceApprovals.Add(approval);
            await db.SaveChangesAsync();

            approvalId = approval.Id;
        }

        using var client1 = _factory.CreateClient();
        using var client2 = _factory.CreateClient();

        var url = $"/api/maintenance-approvals/{approvalId}/approve";

        // Act: Send two HTTP requests concurrently
        var task1 = client1.PostAsJsonAsync(url, new { });
        var task2 = client2.PostAsJsonAsync(url, new { });

        var responses = await Task.WhenAll(task1, task2);

        // Assert 1: Exactly one 200 and one 409
        Assert.Equal(
            1,
            responses.Count(r => r.StatusCode == HttpStatusCode.OK));

        Assert.Equal(
            1,
            responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        // Assert 2: Verify PostgreSQL state
        await using var verifyScope =
            _factory.Services.CreateAsyncScope();

        var verifyDb = verifyScope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var savedApproval = await verifyDb.MaintenanceApprovals
            .AsNoTracking()
            .SingleAsync(x => x.Id == approvalId);

        Assert.Equal("Approved", savedApproval.Status);

        var requests = await verifyDb.MaintenanceRequests
            .AsNoTracking()
            .Where(x =>
                x.PropertyId == propertyId &&
                x.Description == "Water leaking in the bathroom")
            .ToListAsync();

        Assert.Single(requests);

        Assert.Equal(
            requests[0].Id,
            savedApproval.MaintenanceRequestId);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}