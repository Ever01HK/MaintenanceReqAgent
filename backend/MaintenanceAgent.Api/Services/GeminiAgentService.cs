using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Models;
using Microsoft.EntityFrameworkCore;

// Add for handling Idempotency
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MaintenanceAgent.Api.Services;

public class GeminiAgentService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly MaintenanceDbContext _db;
    private readonly ILogger<GeminiAgentService> _logger;

    public GeminiAgentService(
        HttpClient httpClient,
        IConfiguration configuration,
        MaintenanceDbContext db,
        ILogger<GeminiAgentService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _db = db;
        _logger = logger;
    }

    public async Task<object> ProcessAsync(
        int propertyId,
        string message,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (propertyId <= 0 || string.IsNullOrWhiteSpace(message))
        {
            _logger.LogInformation(
                "A valid property ID and message are required {PropertyId}",
                propertyId);
            throw new ArgumentException(
                "A valid property ID and message are required.");
        }

        if (message.Length > 4000)
        {
            _logger.LogInformation(
                "Message is too long {PropertyId}",
                propertyId);
            throw new ArgumentException("Message is too long.");
        }

        var propertyExists = await _db.Properties
            .AnyAsync(p => p.Id == propertyId, cancellationToken);

        if (!propertyExists)
        {
            _logger.LogInformation(
                "Property not found {PropertyId}",
                propertyId);
            throw new KeyNotFoundException("Property not found.");
        }

        _logger.LogInformation(
            "Processing maintenance agent request for PropertyId {PropertyId}",
            propertyId);

        // Checking existing approval before calling GeminiAPI
        idempotencyKey = idempotencyKey?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Length > 100)
        {
            throw new ArgumentException(
                "IdempotencyKey is required and must be 100 characters or fewer.");
        }

        var requestHash = CalculateRequestHash(
            propertyId,
            message);
        
        // Retrieve existing approval
        var existingApproval = await _db.MaintenanceApprovals
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existingApproval != null)
        {
            if (existingApproval.RequestHash != requestHash)
            {
                _logger.LogWarning(
                    "Idempotency conflict for PropertyId {PropertyId}",
                    propertyId);

                throw new IdempotencyConflictException(
                    "The idempotency key has already been used for a different request.");
            }

            _logger.LogInformation(
                "Reusing maintenance approval {ApprovalId} with status {Status}",
                existingApproval.Id,
                existingApproval.Status);

            return CreateAgentResponse(existingApproval, reused: true);
        }

        // Calling Gemini API
        var apiKey = _configuration["Gemini:ApiKey"];
        var model = _configuration["Gemini:Model"];

        if (string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                "Gemini configuration is missing.");
        }

        var requestBody = new
        {
            systemInstruction = new
            {
                parts = new[]
                {
                    new
                    {
                        text = """
                        You are a property maintenance assistant.
                        Analyze the tenant's maintenance issue.
                        Call CreateMaintenanceRequest when the user
                        explicitly asks to create a maintenance request.
                        Choose category from Plumbing, Electrical,
                        Heating, or Other.
                        Choose priority from Low, Normal, High,
                        or Emergency.
                        Do not invent facts.
                        Do not invent a property ID.
                        """
                    }
                }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new
                        {
                            text = $"Property ID: {propertyId}\nMessage: {message}"
                        }
                    }
                }
            },
            tools = new[]
            {
                new
                {
                    functionDeclarations = new[]
                    {
                        new
                        {
                            name = "CreateMaintenanceRequest",
                            description =
                                "Create a property maintenance request.",
                            parameters = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    description = new
                                    {
                                        type = "STRING"
                                    },
                                    category = new
                                    {
                                        type = "STRING",
                                        @enum = new[]
                                        {
                                            "Plumbing",
                                            "Electrical",
                                            "Heating",
                                            "Other"
                                        }
                                    },
                                    priority = new
                                    {
                                        type = "STRING",
                                        @enum = new[]
                                        {
                                            "Low",
                                            "Normal",
                                            "High",
                                            "Emergency"
                                        }
                                    }
                                },
                                required = new[]
                                {
                                    "description",
                                    "category",
                                    "priority"
                                }
                            }
                        }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");

        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = JsonContent.Create(requestBody);

        using var response = await _httpClient.SendAsync(
            request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Gemini API request failed. HTTP Status: {StatusCode}, PropertyId: {PropertyId}",
                (int)response.StatusCode,
                propertyId);

            response.EnsureSuccessStatusCode();
        }   

        // Parsing functionCall
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        var candidates = document.RootElement
            .GetProperty("candidates");

        if (candidates.GetArrayLength() == 0)
        {
            return new { message = "No response from Gemini." };
        }

        var parts = candidates[0]
            .GetProperty("content")
            .GetProperty("parts");

        JsonElement functionCallPart = default;

        foreach (var part in parts.EnumerateArray())
        {
            if (!part.TryGetProperty("functionCall", out var functionCall))
            {
                continue;
            }

            var functionName = functionCall
                .GetProperty("name")
                .GetString();

            if (functionName != "CreateMaintenanceRequest")
            {
                continue;
            }
            // Save original funcctionCall
            functionCallPart = part.Clone();

            var args = functionCall.GetProperty("args");

            var description = args
                .GetProperty("description")
                .GetString();

            var category = args
                .GetProperty("category")
                .GetString();

            var priority = args
                .GetProperty("priority")
                .GetString();

            if (string.IsNullOrWhiteSpace(description) ||
                description.Length > 4000)
            {
                throw new InvalidOperationException(
                    "Invalid maintenance description.");
            }

            if (!new[] { "Plumbing", "Electrical", "Heating", "Other" }
                .Contains(category))
            {
                throw new InvalidOperationException(
                    "Invalid maintenance category.");
            }

            if (!new[] { "Low", "Normal", "High", "Emergency" }
                .Contains(priority))
            {
                throw new InvalidOperationException(
                    "Invalid maintenance priority.");
            }

            // Return Pending ID instead of creating data in DB
            var approval = new MaintenanceApproval
            {
                PropertyId = propertyId,
                Description = description!,
                Category = category!,
                Priority = priority!,
                Status = "Pending",
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash,
                CreatedAt = DateTime.UtcNow
            };

            // Save MaintenanceApprovals
            _db.MaintenanceApprovals.Add(approval);

            try{
                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Maintenance approval {ApprovalId} created for PropertyId {PropertyId}",
                    approval.Id,
                    propertyId);
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is PostgresException pg && 
                        pg.SqlState == PostgresErrorCodes.UniqueViolation &&
                        pg.ConstraintName == "IX_MaintenanceApprovals_IdempotencyKey")
            {
                // In case, the other request is saved with the same key
                _db.Entry(approval).State = EntityState.Detached;

                var savedApproval = await _db.MaintenanceApprovals
                    .AsNoTracking()
                    .FirstAsync(
                        x => x.IdempotencyKey == idempotencyKey, 
                        cancellationToken);
                    
                if (savedApproval.RequestHash != requestHash)
                {
                    throw new IdempotencyConflictException(
                        "The idempotency key has already been used for a different request.");
                }

                _logger.LogInformation(
                    "Concurrent duplicate request reused approval {ApprovalId}",
                    savedApproval.Id);

                return CreateAgentResponse(savedApproval, reused: true);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Database error while saving maintenance approval for PropertyId {PropertyId}",
                    propertyId);

                throw;
            }

            var toolResult = ToApprovalResponse(
                approval,
                reused: false);

            string? finalMessage = null;

            try
            {
                // Send the execution result back to Gemini
                finalMessage = await GenerateFinalResponseAsync(
                    $"Property ID: {propertyId}\nMessage: {message}",
                    functionCallPart,
                    toolResult,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
               throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Gemini final response failed for approval {ApprovalId}",
                    approval.Id);
                
            }

            // return the result
            return CreateAgentResponse(
                approval,
                reused: false,
                message: finalMessage
            );           
        }

        // Return msg why Gemini return the description
        var textParts = new List<string>();

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textElement))
            {
                var text = textElement.GetString();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    textParts.Add(text);
                }
            }
        }

        return new
        {
            message = textParts.Count > 0
                ? string.Join("\n", textParts)
                : "I couldn't process your request.",
            executed = false,
            requiresApproval = false,
            approvalId = (int?)null,
            status = (string?)null,
            reused = false
        };
    }

    // Calculate hash value for checking duplicated request
    private static string CalculateRequestHash(
    int propertyId,
    string message)
    {
        var normalisedMessage = message.Trim();

        var input = $"{propertyId}:{normalisedMessage}";

        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(input));

        return Convert.ToHexString(bytes);
    }

    // Generate response msg whehter it is a duplicated request
    private static object ToApprovalResponse(
    MaintenanceApproval approval,
    bool reused)
    {
        return new
        {
            tool = "CreateMaintenanceRequest",
            executed = false,
            requiresApproval = approval.Status == "Pending",
            approvalId = approval.Id,
            approval.PropertyId,
            approval.Description,
            approval.Category,
            approval.Priority,
            approval.Status,
            maintenanceRequestId = approval.MaintenanceRequestId,
            reused
        };
    }

    private async Task<string> GenerateFinalResponseAsync(
    string originalMessage,
    JsonElement functionCallPart,
    object toolResult,
    CancellationToken cancellationToken)
    {
        var apiKey = _configuration["Gemini:ApiKey"];
        var model = _configuration["Gemini:Model"];

        if (string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                "Gemini configuration is missing.");
        }



        var requestBody = new
        {
            systemInstruction = new
            {
                parts = new[]
                {
                    new
                    {
                        text = """
                        You are a property maintenance assistant.
                        Explain the tool result clearly and concisely.
                        A Pending approval is not a completed maintenance request.
                        Never claim that a request has been approved or executed
                        unless the tool result explicitly confirms it.
                        Do not invent approval IDs, priorities, or statuses.
                        """
                    }
                }
            },

            contents = new object[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = originalMessage }
                    }
                },
                new
                {
                    role = "model",
                    parts = new object[]
                    {
                        functionCallPart
                    }
                },
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new
                        {
                            functionResponse = new
                            {
                                name = "CreateMaintenanceRequest",
                                response = toolResult
                            }
                        }
                    }
                }
            }
        };

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post, url);

        httpRequest.Headers.Add("x-goog-api-key", apiKey);

        httpRequest.Content = JsonContent.Create(requestBody);

        using var response = await _httpClient.SendAsync(
            httpRequest, cancellationToken);

        var responseText = await response.Content.ReadAsStringAsync(
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(responseText);

        var parts = document.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts");

        var messages = new List<string>();

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textElement))
            {
                var text = textElement.GetString();

                if (!string.IsNullOrWhiteSpace(text))
                    messages.Add(text);
            }
        }

        return string.Join("\n", messages);
    }

    private static object CreateAgentResponse(
    MaintenanceApproval approval,
    bool reused,
    string? message = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            message = approval.Status switch
            {
                "Pending" =>
                    $"Maintenance approval #{approval.Id} is awaiting review.",

                "Approved" =>
                    $"Maintenance approval #{approval.Id} has been approved.",

                "Rejected" =>
                    $"Maintenance approval #{approval.Id} has been rejected.",

                _ =>
                    $"Maintenance approval #{approval.Id} has status: {approval.Status}."
            };
        }

        return new
        {
            message,
            tool = "CreateMaintenanceRequest",
            executed = approval.Status == "Approved"
                    && approval.MaintenanceRequestId != null,
            requiresApproval = approval.Status == "Pending",
            approvalId = approval.Id,
            propertyId = approval.PropertyId,
            description = approval.Description,
            category = approval.Category,
            priority = approval.Priority,
            status = approval.Status,
            maintenanceRequestId = approval.MaintenanceRequestId,
            reused
        };
    }
}