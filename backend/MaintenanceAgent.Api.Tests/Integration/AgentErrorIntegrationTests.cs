using System.Net;
using System.Net.Http.Json;
using MaintenanceAgent.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MaintenanceAgent.Api.Tests.Integration;

public class AgentErrorIntegrationTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public AgentErrorIntegrationTests(
        IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Process_WithInvalidPropertyId_Returns400()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/agent/process",
            new
            {
                propertyId = 0,
                message = "Water leaking",
                idempotencyKey = Guid.NewGuid().ToString()
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Process_WithEmptyMessage_Returns400()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/agent/process",
            new
            {
                propertyId = 1,
                message = "",
                idempotencyKey = Guid.NewGuid().ToString()
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Process_WithNonExistentProperty_Returns404()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/agent/process",
            new
            {
                propertyId = int.MaxValue,
                message = "Water leaking",
                idempotencyKey = Guid.NewGuid().ToString()
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}