using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    private async Task<(int approvalId, int propertyId, string description)> CreatePendingApprovalAsync(
        string description)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var property = new Property
        {
            Address = "303 Concurrent Test Street",
            TenantName = "Concurrent Test Tenant"
        };

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        var approval = new MaintenanceApproval
        {
            PropertyId = property.Id,
            Description = description,
            Category = "Plumbing",
            Priority = "High",
            Status = "Pending",
            IdempotencyKey = Guid.NewGuid().ToString(),
            RequestHash = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow
        };

        db.MaintenanceApprovals.Add(approval);
        await db.SaveChangesAsync();

        return (approval.Id, property.Id, description);
    }

    [Fact]
    public async Task ConcurrentApprove_OnlyOneMaintenanceRequestIsCreated()
    {
        // Arrange
        var (approvalId, propertyId, description) =
            await CreatePendingApprovalAsync("Water leaking in the bathroom");

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

        var conflictResponse = responses
            .Single(r => r.StatusCode == HttpStatusCode.Conflict);

        using var conflictJson = await conflictResponse.Content
            .ReadFromJsonAsync<JsonDocument>();

        Assert.NotNull(conflictJson);

        var root = conflictJson.RootElement;
        Assert.Equal(
            "This approval has already been processed.",
            root.GetProperty("message").GetString());
        Assert.Equal("Approved", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("maintenanceRequestId", out var requestIdElement));
        Assert.Equal(JsonValueKind.Number, requestIdElement.ValueKind);
        Assert.True(requestIdElement.GetInt32() > 0);

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
                x.Description == description)
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

    [Fact]
    public async Task ConcurrentApproveAndReject_OnlyOneStateTransitionSucceeds()
    {
        // Arrange
        var (approvalId, propertyId, description) =
            await CreatePendingApprovalAsync("Sink overflow in kitchen");

        using var approveClient = _factory.CreateClient();
        using var rejectClient = _factory.CreateClient();

        var approveTask = approveClient.PostAsync(
            $"/api/maintenance-approvals/{approvalId}/approve",
            null);

        var rejectTask = rejectClient.PostAsync(
            $"/api/maintenance-approvals/{approvalId}/reject",
            null);

        await Task.WhenAll(approveTask, rejectTask);

        var approveResponse = approveTask.Result;
        var rejectResponse = rejectTask.Result;

        // Assert: exactly one successful transition, the other conflicts
        Assert.Contains(
            approveResponse.StatusCode,
            new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        Assert.Contains(
            rejectResponse.StatusCode,
            new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });

        Assert.Equal(1, new[]
        {
            approveResponse.StatusCode,
            rejectResponse.StatusCode
        }.Count(s => s == HttpStatusCode.OK));

        Assert.Equal(1, new[]
        {
            approveResponse.StatusCode,
            rejectResponse.StatusCode
        }.Count(s => s == HttpStatusCode.Conflict));

        if (approveResponse.StatusCode == HttpStatusCode.Conflict)
        {
            using var approveConflict = await approveResponse.Content
                .ReadFromJsonAsync<JsonDocument>();

            Assert.NotNull(approveConflict);

            var conflictRoot = approveConflict.RootElement;

            Assert.Equal(
                "This approval has already been processed.",
                conflictRoot.GetProperty("message").GetString());
            Assert.Equal("Rejected", conflictRoot.GetProperty("status").GetString());
            Assert.True(conflictRoot.TryGetProperty("maintenanceRequestId", out var requestIdElement));
            Assert.Equal(JsonValueKind.Null, requestIdElement.ValueKind);
        }

        if (rejectResponse.StatusCode == HttpStatusCode.Conflict)
        {
            using var rejectConflict = await rejectResponse.Content
                .ReadFromJsonAsync<JsonDocument>();

            Assert.NotNull(rejectConflict);

            Assert.Equal(
                "This approval has already been processed.",
                rejectConflict.RootElement.GetProperty("message").GetString());
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();

        var verifyDb = verifyScope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var savedApproval = await verifyDb.MaintenanceApprovals
            .AsNoTracking()
            .SingleAsync(x => x.Id == approvalId);

        Assert.Contains(savedApproval.Status, new[] { "Approved", "Rejected" });

        var requests = await verifyDb.MaintenanceRequests
            .AsNoTracking()
            .Where(x =>
                x.PropertyId == propertyId &&
                x.Description == description)
            .ToListAsync();

        if (savedApproval.Status == "Approved")
        {
            Assert.Single(requests);
            Assert.NotNull(savedApproval.MaintenanceRequestId);
            Assert.Equal(requests[0].Id, savedApproval.MaintenanceRequestId);
        }
        else
        {
            Assert.Equal("Rejected", savedApproval.Status);
            Assert.Empty(requests);
            Assert.Null(savedApproval.MaintenanceRequestId);
        }

        approveResponse.Dispose();
        rejectResponse.Dispose();
    }

    [Fact]
    public async Task ConcurrentReject_OneSucceedsOneConflicts_FinalStatusRejected_NoRequest()
    {
        // Arrange
        var (approvalId, propertyId, description) =
            await CreatePendingApprovalAsync("Bathroom door lock broken");

        using var client1 = _factory.CreateClient();
        using var client2 = _factory.CreateClient();

        var reject1 = client1.PostAsync(
            $"/api/maintenance-approvals/{approvalId}/reject",
            null);

        var reject2 = client2.PostAsync(
            $"/api/maintenance-approvals/{approvalId}/reject",
            null);

        var responses = await Task.WhenAll(reject1, reject2);

        Assert.Equal(
            1,
            responses.Count(r => r.StatusCode == HttpStatusCode.OK));

        Assert.Equal(
            1,
            responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var conflict = responses
            .Single(r => r.StatusCode == HttpStatusCode.Conflict);

        using var conflictJson = await conflict.Content
            .ReadFromJsonAsync<JsonDocument>();

        Assert.NotNull(conflictJson);
        Assert.Equal(
            "This approval has already been processed.",
            conflictJson.RootElement.GetProperty("message").GetString());

        await using var verifyScope = _factory.Services.CreateAsyncScope();

        var verifyDb = verifyScope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var approval = await verifyDb.MaintenanceApprovals
            .AsNoTracking()
            .SingleAsync(x => x.Id == approvalId);

        Assert.Equal("Rejected", approval.Status);
        Assert.Null(approval.MaintenanceRequestId);

        var requestCount = await verifyDb.MaintenanceRequests
            .AsNoTracking()
            .CountAsync(x =>
                x.PropertyId == propertyId &&
                x.Description == description);

        Assert.Equal(0, requestCount);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}