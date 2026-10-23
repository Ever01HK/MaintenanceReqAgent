using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MaintenanceAgent.Api.Tests.Integration;

public class MaintenanceApprovalsListIntegrationTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public MaintenanceApprovalsListIntegrationTests(
        IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_WithoutStatusFilter_ReturnsAllApprovals()
    {
        await ResetDatabaseAsync();

        var propertyId = await CreatePropertyAsync("all-list");

        var approvals = await SeedApprovalsAsync(
            propertyId,
            new[]
            {
                ApprovalSeed.Pending("all-pending", Utc(2026, 10, 24, 10, 0, 0)),
                ApprovalSeed.Approved("all-approved", Utc(2026, 10, 24, 11, 0, 0)),
                ApprovalSeed.Rejected("all-rejected", Utc(2026, 10, 24, 12, 0, 0))
            });

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            "/api/maintenance-approvals?page=1&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);

        var root = json.RootElement;
        Assert.Equal(3, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(100, root.GetProperty("pageSize").GetInt32());

        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);

        Assert.Equal(
            approvals
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .Select(x => x.Id)
                .ToArray(),
            items.Select(x => x.GetProperty("id").GetInt32()).ToArray());

        Assert.Contains(items, x => x.GetProperty("status").GetString() == "Pending");
        Assert.Contains(items, x => x.GetProperty("status").GetString() == "Approved");
        Assert.Contains(items, x => x.GetProperty("status").GetString() == "Rejected");
    }

    [Fact]
    public async Task List_WithPendingFilter_ReturnsOnlyPending()
    {
        await ResetDatabaseAsync();

        var propertyId = await CreatePropertyAsync("pending-filter");

        await SeedApprovalsAsync(
            propertyId,
            new[]
            {
                ApprovalSeed.Pending("pending-only-a", Utc(2026, 10, 24, 10, 0, 0)),
                ApprovalSeed.Pending("pending-only-b", Utc(2026, 10, 24, 9, 0, 0)),
                ApprovalSeed.Approved("pending-filter-approved", Utc(2026, 10, 24, 8, 0, 0)),
                ApprovalSeed.Rejected("pending-filter-rejected", Utc(2026, 10, 24, 7, 0, 0))
            });

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            "/api/maintenance-approvals?status=Pending&page=1&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);

        var items = json.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToList();

        Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal("Pending", item.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task List_WithApprovedFilter_ReturnsOnlyApproved()
    {
        await ResetDatabaseAsync();

        var propertyId = await CreatePropertyAsync("approved-filter");

        await SeedApprovalsAsync(
            propertyId,
            new[]
            {
                ApprovalSeed.Approved("approved-only-a", Utc(2026, 10, 24, 10, 0, 0)),
                ApprovalSeed.Approved("approved-only-b", Utc(2026, 10, 24, 9, 0, 0)),
                ApprovalSeed.Pending("approved-filter-pending", Utc(2026, 10, 24, 8, 0, 0)),
                ApprovalSeed.Rejected("approved-filter-rejected", Utc(2026, 10, 24, 7, 0, 0))
            });

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            "/api/maintenance-approvals?status=Approved&page=1&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);

        var items = json.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToList();

        Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal("Approved", item.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task List_WithRejectedFilter_ReturnsOnlyRejected()
    {
        await ResetDatabaseAsync();

        var propertyId = await CreatePropertyAsync("rejected-filter");

        await SeedApprovalsAsync(
            propertyId,
            new[]
            {
                ApprovalSeed.Rejected("rejected-only-a", Utc(2026, 10, 24, 10, 0, 0)),
                ApprovalSeed.Rejected("rejected-only-b", Utc(2026, 10, 24, 9, 0, 0)),
                ApprovalSeed.Pending("rejected-filter-pending", Utc(2026, 10, 24, 8, 0, 0)),
                ApprovalSeed.Approved("rejected-filter-approved", Utc(2026, 10, 24, 7, 0, 0))
            });

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            "/api/maintenance-approvals?status=Rejected&page=1&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);

        var items = json.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToList();

        Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal("Rejected", item.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task List_WithNoMatchingStatus_ReturnsEmptyItemsAndZeroCount()
    {
        await ResetDatabaseAsync();

        var propertyId = await CreatePropertyAsync("empty-filter");

        await SeedApprovalsAsync(
            propertyId,
            new[]
            {
                ApprovalSeed.Approved("empty-approved", Utc(2026, 10, 24, 10, 0, 0)),
                ApprovalSeed.Rejected("empty-rejected", Utc(2026, 10, 24, 9, 0, 0))
            });

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            "/api/maintenance-approvals?status=Pending&page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);

        Assert.Equal(0, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_OrdersByCreatedAtDescending_ThenByIdDescending()
    {
        await ResetDatabaseAsync();

        var propertyId = await CreatePropertyAsync("ordering");
        var sameTimestamp = Utc(2026, 10, 24, 11, 0, 0);

        var seeded = await SeedApprovalsAsync(
            propertyId,
            new[]
            {
                ApprovalSeed.Pending("older", Utc(2026, 10, 24, 10, 0, 0)),
                ApprovalSeed.Pending("same-time-a", sameTimestamp),
                ApprovalSeed.Pending("same-time-b", sameTimestamp),
                ApprovalSeed.Pending("newest", Utc(2026, 10, 24, 12, 0, 0))
            });

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            "/api/maintenance-approvals?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);

        var items = json.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToList();

        var actualOrder = items
            .Select(item => item.GetProperty("id").GetInt32())
            .ToArray();

        var expectedOrder = seeded
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => x.Id)
            .ToArray();

        Assert.Equal(expectedOrder, actualOrder);
    }

    [Fact]
    public async Task List_Pagination_WithPageAndPageSize_ReturnsExpectedSlice()
    {
        await ResetDatabaseAsync();

        var propertyId = await CreatePropertyAsync("pagination");

        var seeded = await SeedApprovalsAsync(
            propertyId,
            new[]
            {
                ApprovalSeed.Pending("p1", Utc(2026, 10, 24, 15, 0, 0)),
                ApprovalSeed.Pending("p2", Utc(2026, 10, 24, 14, 0, 0)),
                ApprovalSeed.Pending("p3", Utc(2026, 10, 24, 13, 0, 0)),
                ApprovalSeed.Pending("p4", Utc(2026, 10, 24, 12, 0, 0)),
                ApprovalSeed.Pending("p5", Utc(2026, 10, 24, 11, 0, 0))
            });

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            "/api/maintenance-approvals?page=2&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);

        Assert.Equal(5, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("pageSize").GetInt32());

        var items = json.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToList();

        Assert.Equal(2, items.Count);

        var expectedPage2Ids = seeded
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip(2)
            .Take(2)
            .Select(x => x.Id)
            .ToArray();

        Assert.Equal(
            expectedPage2Ids,
            items.Select(x => x.GetProperty("id").GetInt32()).ToArray());
    }

    [Fact]
    public async Task List_TotalCount_IsConsistentAcrossPages()
    {
        await ResetDatabaseAsync();

        var propertyId = await CreatePropertyAsync("total-count");

        await SeedApprovalsAsync(
            propertyId,
            new[]
            {
                ApprovalSeed.Pending("tc-1", Utc(2026, 10, 24, 15, 0, 0)),
                ApprovalSeed.Pending("tc-2", Utc(2026, 10, 24, 14, 0, 0)),
                ApprovalSeed.Pending("tc-3", Utc(2026, 10, 24, 13, 0, 0)),
                ApprovalSeed.Pending("tc-4", Utc(2026, 10, 24, 12, 0, 0)),
                ApprovalSeed.Pending("tc-5", Utc(2026, 10, 24, 11, 0, 0))
            });

        using var client = _factory.CreateClient();

        using var page1 = await client.GetAsync(
            "/api/maintenance-approvals?page=1&pageSize=2");
        using var page3 = await client.GetAsync(
            "/api/maintenance-approvals?page=3&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, page1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, page3.StatusCode);

        using var page1Json = await page1.Content.ReadFromJsonAsync<JsonDocument>();
        using var page3Json = await page3.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.NotNull(page1Json);
        Assert.NotNull(page3Json);

        Assert.Equal(5, page1Json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(5, page3Json.RootElement.GetProperty("totalCount").GetInt32());

        Assert.Equal(2, page1Json.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(1, page3Json.RootElement.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    [InlineData(1, 101)]
    public async Task List_WithInvalidPageOrPageSize_ReturnsBadRequest(
        int page,
        int pageSize)
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            $"/api/maintenance-approvals?page={page}&pageSize={pageSize}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);
        Assert.Equal(
            "Page must be >= 1 and pageSize must be between 1 and 100.",
            json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task List_ReflectsUpdatedStatuses_AfterApproveAndReject()
    {
        await ResetDatabaseAsync();

        var propertyId = await CreatePropertyAsync("status-reflect");

        var approvals = await SeedApprovalsAsync(
            propertyId,
            new[]
            {
                ApprovalSeed.Pending("to-approve", Utc(2026, 10, 24, 10, 0, 0)),
                ApprovalSeed.Pending("to-reject", Utc(2026, 10, 24, 9, 0, 0))
            });

        var approveId = approvals.Single(x => x.Description == "to-approve").Id;
        var rejectId = approvals.Single(x => x.Description == "to-reject").Id;

        using var client = _factory.CreateClient();

        using var approveResponse = await client.PostAsync(
            $"/api/maintenance-approvals/{approveId}/approve",
            null);

        using var rejectResponse = await client.PostAsync(
            $"/api/maintenance-approvals/{rejectId}/reject",
            null);

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);

        // Verify database state before list assertions.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<MaintenanceDbContext>();

            var approved = await db.MaintenanceApprovals
                .AsNoTracking()
                .SingleAsync(x => x.Id == approveId);

            var rejected = await db.MaintenanceApprovals
                .AsNoTracking()
                .SingleAsync(x => x.Id == rejectId);

            Assert.Equal("Approved", approved.Status);
            Assert.NotNull(approved.MaintenanceRequestId);

            Assert.Equal("Rejected", rejected.Status);
            Assert.Null(rejected.MaintenanceRequestId);

            var createdRequestCount = await db.MaintenanceRequests
                .AsNoTracking()
                .CountAsync(x => x.PropertyId == propertyId);

            Assert.Equal(1, createdRequestCount);
        }

        using var pendingList = await client.GetAsync(
            "/api/maintenance-approvals?status=Pending&page=1&pageSize=20");
        using var approvedList = await client.GetAsync(
            "/api/maintenance-approvals?status=Approved&page=1&pageSize=20");
        using var rejectedList = await client.GetAsync(
            "/api/maintenance-approvals?status=Rejected&page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, pendingList.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approvedList.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejectedList.StatusCode);

        using var pendingJson = await pendingList.Content.ReadFromJsonAsync<JsonDocument>();
        using var approvedJson = await approvedList.Content.ReadFromJsonAsync<JsonDocument>();
        using var rejectedJson = await rejectedList.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.NotNull(pendingJson);
        Assert.NotNull(approvedJson);
        Assert.NotNull(rejectedJson);

        Assert.Equal(0, pendingJson.RootElement.GetProperty("totalCount").GetInt32());

        var approvedItems = approvedJson.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToList();

        var rejectedItems = rejectedJson.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToList();

        Assert.Contains(approvedItems, item => item.GetProperty("id").GetInt32() == approveId);
        Assert.Contains(rejectedItems, item => item.GetProperty("id").GetInt32() == rejectId);
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        await db.MaintenanceApprovals.ExecuteDeleteAsync();
        await db.MaintenanceRequests.ExecuteDeleteAsync();
        await db.Properties.ExecuteDeleteAsync();
    }

    private async Task<int> CreatePropertyAsync(string suffix)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var property = new Property
        {
            Address = $"900 List Test Street {suffix}",
            TenantName = $"List Test Tenant {suffix}"
        };

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        return property.Id;
    }

    private async Task<List<SeededApproval>> SeedApprovalsAsync(
        int propertyId,
        IEnumerable<ApprovalSeed> seeds)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<MaintenanceDbContext>();

        var result = new List<SeededApproval>();

        foreach (var seed in seeds)
        {
            int? requestId = null;

            if (seed.Status == "Approved")
            {
                var request = new MaintenanceRequest
                {
                    PropertyId = propertyId,
                    Description = $"request-{seed.Description}",
                    Category = seed.Category,
                    Priority = seed.Priority,
                    Status = "Open",
                    CreatedAt = seed.CreatedAt
                };

                db.MaintenanceRequests.Add(request);
                await db.SaveChangesAsync();

                requestId = request.Id;
            }

            var approval = new MaintenanceApproval
            {
                PropertyId = propertyId,
                Description = seed.Description,
                Category = seed.Category,
                Priority = seed.Priority,
                Status = seed.Status,
                CreatedAt = seed.CreatedAt,
                ReviewedAt = seed.Status == "Pending" ? null : seed.CreatedAt.AddMinutes(5),
                MaintenanceRequestId = requestId,
                IdempotencyKey = $"list-{Guid.NewGuid()}",
                RequestHash = Guid.NewGuid().ToString("N")
            };

            db.MaintenanceApprovals.Add(approval);
            await db.SaveChangesAsync();

            result.Add(new SeededApproval(
                approval.Id,
                approval.Description,
                approval.Status,
                approval.CreatedAt));
        }

        return result;
    }

    private static DateTime Utc(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second)
    {
        return new DateTime(
            year,
            month,
            day,
            hour,
            minute,
            second,
            DateTimeKind.Utc);
    }

    private sealed record ApprovalSeed(
        string Status,
        string Description,
        DateTime CreatedAt,
        string Category,
        string Priority)
    {
        public static ApprovalSeed Pending(
            string description,
            DateTime createdAt)
        {
            return new ApprovalSeed(
                "Pending",
                description,
                createdAt,
                "Plumbing",
                "Normal");
        }

        public static ApprovalSeed Approved(
            string description,
            DateTime createdAt)
        {
            return new ApprovalSeed(
                "Approved",
                description,
                createdAt,
                "Electrical",
                "High");
        }

        public static ApprovalSeed Rejected(
            string description,
            DateTime createdAt)
        {
            return new ApprovalSeed(
                "Rejected",
                description,
                createdAt,
                "Other",
                "Low");
        }
    }

    private sealed record SeededApproval(
        int Id,
        string Description,
        string Status,
        DateTime CreatedAt);
}
