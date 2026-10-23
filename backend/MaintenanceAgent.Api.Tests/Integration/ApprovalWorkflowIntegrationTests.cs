using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MaintenanceAgent.Api.Tests.Integration;

public class ApprovalWorkflowIntegrationTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public ApprovalWorkflowIntegrationTests(
        IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<int> CreatePendingApprovalAsync()
    {
        await using var scope =
            _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var property = new Property
        {
            Address = "505 Workflow Test Street",
            TenantName = "Workflow Test Tenant"
        };

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        var approval = new MaintenanceApproval
        {
            PropertyId = property.Id,
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

        return approval.Id;
    }

    [Fact]
    public async Task Approve_PendingApproval_CreatesMaintenanceRequest()
    {
        // Arrange
        var approvalId = await CreatePendingApprovalAsync();

        using var client = _factory.CreateClient();

        // Act: Approve through HTTP
        using var response = await client.PostAsync(
            $"/api/maintenance-approvals/{approvalId}/approve",
            null);

        // Assert: HTTP 200
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify actual PostgreSQL data
        await using var scope =
            _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var approval = await db.MaintenanceApprovals
            .AsNoTracking()
            .SingleAsync(x => x.Id == approvalId);

        Assert.Equal("Approved", approval.Status);
        Assert.NotNull(approval.MaintenanceRequestId);

        var request = await db.MaintenanceRequests
            .AsNoTracking()
            .SingleAsync(x =>
                x.Id == approval.MaintenanceRequestId);

        Assert.Equal(approval.PropertyId, request.PropertyId);
        Assert.Equal(approval.Description, request.Description);
        Assert.Equal(approval.Category, request.Category);
        Assert.Equal(approval.Priority, request.Priority);

        // Act: Try to approve again
        using var secondResponse = await client.PostAsync(
            $"/api/maintenance-approvals/{approvalId}/approve",
            null);

        Assert.Equal(
            HttpStatusCode.Conflict,
            secondResponse.StatusCode);

        // Ensure no duplicate request was created
        var requestCount = await db.MaintenanceRequests
            .AsNoTracking()
            .CountAsync(x =>
                x.PropertyId == approval.PropertyId &&
                x.Description == approval.Description);

        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task Reject_PendingApproval_DoesNotCreateMaintenanceRequest()
    {
        // Arrange
        var approvalId = await CreatePendingApprovalAsync();

        using var client = _factory.CreateClient();

        // Act: Reject through HTTP
        using var response = await client.PostAsync(
            $"/api/maintenance-approvals/{approvalId}/reject",
            null);

        // Assert: HTTP 200
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope =
            _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var approval = await db.MaintenanceApprovals
            .AsNoTracking()
            .SingleAsync(x => x.Id == approvalId);

        Assert.Equal("Rejected", approval.Status);
        Assert.Null(approval.MaintenanceRequestId);

        var requestCount = await db.MaintenanceRequests
            .AsNoTracking()
            .CountAsync(x =>
                x.PropertyId == approval.PropertyId &&
                x.Description == approval.Description);

        Assert.Equal(0, requestCount);

        // Act: Try to reject again
        using var secondResponse = await client.PostAsync(
            $"/api/maintenance-approvals/{approvalId}/reject",
            null);

        Assert.Equal(
            HttpStatusCode.Conflict,
            secondResponse.StatusCode);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task Approval_WhenIdDoesNotExist_Returns404(
        string action)
    {
        using var client = _factory.CreateClient();

        // IDs generated by PostgreSQL are positive.
        // A negative ID cannot match a generated approval.
        using var response = await client.PostAsync(
            $"/api/maintenance-approvals/-1/{action}",
            null);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }
}