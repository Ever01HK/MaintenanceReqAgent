using System.Net;
using System.Text;
using System.Text.Json;
using MaintenanceAgent.Api.Controllers;
using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Dtos;
using MaintenanceAgent.Api.Models;
using MaintenanceAgent.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Microsoft.AspNetCore.Mvc;

namespace MaintenanceAgent.Api.Tests;

public class GeminiAgentServiceScenarioTests
{
    [Fact]
    public async Task NewIdempotencyKey_CreatesPendingApproval_AndReturnsNaturalLanguageMessage()
    {
        var handler = new QueueHttpMessageHandler(new[]
        {
          JsonResponse(HttpStatusCode.OK, FirstGeminiFunctionCallResponse()),
          JsonResponse(HttpStatusCode.OK, FinalGeminiTextResponse("Created a pending approval request."))
        });

        await using var db = CreateDbContext();
        SeedProperty(db, propertyId: 1);

        var service = CreateService(db, handler);

        var result = await service.ProcessAsync(1, "The bathroom sink is leaking", "key-001");
        var json = ToJson(result);

        Assert.Equal("Pending", json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("requiresApproval").GetBoolean());
        Assert.False(json.GetProperty("reused").GetBoolean());
        Assert.Equal("Created a pending approval request.", json.GetProperty("message").GetString());

        var approval = await db.MaintenanceApprovals.SingleAsync();
        Assert.Equal("Pending", approval.Status);
        Assert.Equal("key-001", approval.IdempotencyKey);
    }

    [Fact]
    public async Task SameKeyAndSameContent_ReusesApprovalId_WithReusedTrue()
    {
        var handler = new QueueHttpMessageHandler(new[]
        {
            JsonResponse(HttpStatusCode.OK, FirstGeminiFunctionCallResponse()),
          JsonResponse(HttpStatusCode.OK, FinalGeminiTextResponse("Created a pending approval request."))
        });

        await using var db = CreateDbContext();
        SeedProperty(db, propertyId: 1);

        var service = CreateService(db, handler);

        var first = await service.ProcessAsync(1, "The boiler is not working", "key-002");
        var firstJson = ToJson(first);

        var second = await service.ProcessAsync(1, "The boiler is not working", "key-002");
        var secondJson = ToJson(second);

        Assert.Equal(firstJson.GetProperty("approvalId").GetInt32(), secondJson.GetProperty("approvalId").GetInt32());
        Assert.True(secondJson.GetProperty("reused").GetBoolean());

        // First request uses 2 Gemini calls. Second request should skip Gemini entirely.
        Assert.Equal(2, handler.SendCount);
    }

    [Fact]
    public async Task SameKeyWithDifferentContent_ThrowsIdempotencyConflictException()
    {
        var handler = new QueueHttpMessageHandler(new[]
        {
            JsonResponse(HttpStatusCode.OK, FirstGeminiFunctionCallResponse()),
          JsonResponse(HttpStatusCode.OK, FinalGeminiTextResponse("Created a pending approval request."))
        });

        await using var db = CreateDbContext();
        SeedProperty(db, propertyId: 1);

        var service = CreateService(db, handler);

        await service.ProcessAsync(1, "Sink drain is clogged", "key-003");

        await Assert.ThrowsAsync<IdempotencyConflictException>(async () =>
          await service.ProcessAsync(1, "The light is flickering", "key-003"));
    }

    [Fact]
    public async Task SecondGeminiCallFails_PersistsApproval_AndReturnsDefaultMessage()
    {
        var handler = new QueueHttpMessageHandler(new[]
        {
            JsonResponse(HttpStatusCode.OK, FirstGeminiFunctionCallResponse()),
            JsonResponse(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}")
        });

        await using var db = CreateDbContext();
        SeedProperty(db, propertyId: 1);

        var service = CreateService(db, handler);

        var result = await service.ProcessAsync(1, "Toilet leak in bathroom", "key-004");
        var json = ToJson(result);

        Assert.Equal("Pending", json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("requiresApproval").GetBoolean());
        Assert.Equal("Maintenance approval #1 is awaiting review.", json.GetProperty("message").GetString());

        var approval = await db.MaintenanceApprovals.SingleAsync();
        Assert.Equal("Pending", approval.Status);
        Assert.Equal("Toilet leak in bathroom", approval.Description);
    }

    [Fact]
    public async Task GeneralMaintenanceQuestion_ReturnsGeminiTextWithoutFunctionCall()
    {
        var handler = new QueueHttpMessageHandler(new[]
        {
          JsonResponse(HttpStatusCode.OK, FirstGeminiTextOnlyResponse("It is recommended to replace the filter every 3 months."))
        });

        await using var db = CreateDbContext();
        SeedProperty(db, propertyId: 1);

        var service = CreateService(db, handler);

        var result = await service.ProcessAsync(1, "I want to know the AC filter replacement cycle", "key-005");
        var json = ToJson(result);

        Assert.False(json.GetProperty("requiresApproval").GetBoolean());
        Assert.Equal("It is recommended to replace the filter every 3 months.", json.GetProperty("message").GetString());
        Assert.Equal(0, await db.MaintenanceApprovals.CountAsync());
    }

    [Theory]
    [InlineData("Approved", 123, true, false)]
    [InlineData("Rejected", null, false, false)]
    public async Task ReRequestAfterApprovalOrReject_ReflectsLatestStatus(
        string targetStatus,
        int? maintenanceRequestId,
        bool expectedExecuted,
        bool expectedRequiresApproval)
    {
        var handler = new QueueHttpMessageHandler(new[]
        {
            JsonResponse(HttpStatusCode.OK, FirstGeminiFunctionCallResponse()),
            JsonResponse(HttpStatusCode.OK, FinalGeminiTextResponse("Created a pending approval request."))
        });

        await using var db = CreateDbContext();
        SeedProperty(db, propertyId: 1);

        var service = CreateService(db, handler);

        var first = await service.ProcessAsync(1, "Kitchen faucet leak", "key-006");
        var firstJson = ToJson(first);
        var approvalId = firstJson.GetProperty("approvalId").GetInt32();

        var approval = await db.MaintenanceApprovals.SingleAsync(x => x.Id == approvalId);
        approval.Status = targetStatus;
        approval.MaintenanceRequestId = maintenanceRequestId;
        await db.SaveChangesAsync();

        var second = await service.ProcessAsync(1, "Kitchen faucet leak", "key-006");
        var secondJson = ToJson(second);

        Assert.Equal(targetStatus, secondJson.GetProperty("status").GetString());
        Assert.Equal(expectedExecuted, secondJson.GetProperty("executed").GetBoolean());
        Assert.Equal(expectedRequiresApproval, secondJson.GetProperty("requiresApproval").GetBoolean());
        Assert.True(secondJson.GetProperty("reused").GetBoolean());

        // No extra Gemini calls for re-request after status update.
        Assert.Equal(2, handler.SendCount);
    }

      [Fact]
      public async Task Controller_Process_MapsIdempotencyConflictToHttp409()
      {
        var handler = new QueueHttpMessageHandler(new[]
        {
          JsonResponse(HttpStatusCode.OK, FirstGeminiFunctionCallResponse()),
          JsonResponse(HttpStatusCode.OK, FinalGeminiTextResponse("Created a pending approval request."))
        });

        await using var db = CreateDbContext();
        SeedProperty(db, propertyId: 1);

        var service = CreateService(db, handler);
        var controller = new AgentController(service);

        await controller.Process(new AgentRequest
        {
          PropertyId = 1,
          Message = "Stucking sink drain",
          IdempotencyKey = "key-007"
        }, CancellationToken.None);

        var result = await controller.Process(
        new AgentRequest
        {
            PropertyId = 1,
          Message = "Blinking a light",
          IdempotencyKey = "key-007"
        },
        CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);

        Assert.Equal(409, conflict.StatusCode);
      }

    private static MaintenanceDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MaintenanceDbContext(options);
    }

    private static void SeedProperty(MaintenanceDbContext db, int propertyId)
    {
        db.Properties.Add(new Property
        {
            Id = propertyId,
            Address = "101 Main St",
            TenantName = "Kim Tenant"
        });
        db.SaveChanges();
    }

    private static GeminiAgentService CreateService(
        MaintenanceDbContext db,
        QueueHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "dummy-key",
                ["Gemini:Model"] = "gemini-2.5-flash"
            })
            .Build();

        return new GeminiAgentService(
            httpClient,
            config,
            db,
            NullLogger<GeminiAgentService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static JsonElement ToJson(object value)
    {
        return JsonSerializer.SerializeToElement(value);
    }

    private static string FirstGeminiFunctionCallResponse()
    {
        return """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "functionCall": {
                      "name": "CreateMaintenanceRequest",
                      "args": {
                        "description": "Toilet leak in bathroom",
                        "category": "Plumbing",
                        "priority": "High"
                      }
                    }
                  }
                ]
              }
            }
          ]
        }
        """;
    }

    private static string FinalGeminiTextResponse(string text)
    {
        return $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  { "text": "{{text}}" }
                ]
              }
            }
          ]
        }
        """;
    }

    private static string FirstGeminiTextOnlyResponse(string text)
    {
        return $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  { "text": "{{text}}" }
                ]
              }
            }
          ]
        }
        """;
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public QueueHttpMessageHandler(IEnumerable<HttpResponseMessage> responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No prepared HTTP response remains.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
