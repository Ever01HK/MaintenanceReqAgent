using System.Net;
using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MaintenanceAgent.Api.Tests.Integration;

public class TransactionRollbackIntegrationTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public TransactionRollbackIntegrationTests(
        IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Approve_WhenMaintenanceRequestInsertFails_RollsBackTransaction()
    {
        // Arrange: Create a Pending approval
        int approvalId;
        int propertyId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<MaintenanceDbContext>();

            var property = new Property
            {
                Address = "404 Rollback Test Street",
                TenantName = "Rollback Test Tenant"
            };

            db.Properties.Add(property);
            await db.SaveChangesAsync();

            propertyId = property.Id;

            var approval = new MaintenanceApproval
            {
                PropertyId = propertyId,
                Description = "Broken water pipe",
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

            // Force INSERT failure in the test database
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "MaintenanceRequests"
                ADD CONSTRAINT "CK_Test_RejectAllInserts"
                CHECK (FALSE);
                """);
        }

        try
        {
            // Act: Send a real HTTP approval request
            using var client = _factory.CreateClient();

            var response = await client.PostAsync(
                $"/api/maintenance-approvals/{approvalId}/approve",
                null);

            // Assert 1: The API reports a server error
            Assert.Equal(
                HttpStatusCode.InternalServerError,
                response.StatusCode);

            // Assert 2: Verify the database after rollback
            await using var verifyScope =
                _factory.Services.CreateAsyncScope();

            var verifyDb = verifyScope.ServiceProvider
                .GetRequiredService<MaintenanceDbContext>();

            var savedApproval = await verifyDb.MaintenanceApprovals
                .AsNoTracking()
                .SingleAsync(x => x.Id == approvalId);

            Assert.Equal("Pending", savedApproval.Status);
            Assert.Null(savedApproval.MaintenanceRequestId);

            var requestCount = await verifyDb.MaintenanceRequests
                .AsNoTracking()
                .CountAsync(x =>
                    x.PropertyId == propertyId &&
                    x.Description == "Broken water pipe");

            Assert.Equal(0, requestCount);
        }
        finally
        {
            // Remove the temporary test constraint
            await using var cleanupScope =
                _factory.Services.CreateAsyncScope();

            var cleanupDb = cleanupScope.ServiceProvider
                .GetRequiredService<MaintenanceDbContext>();

            await cleanupDb.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "MaintenanceRequests"
                DROP CONSTRAINT IF EXISTS "CK_Test_RejectAllInserts";
                """);
        }
    }
}