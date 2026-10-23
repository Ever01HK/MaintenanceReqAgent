
using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using MaintenanceAgent.Api.Exceptions;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();
// Gemini API Client
builder.Services.AddHttpClient<GeminiAgentService>();
// Global Exception Handling
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var connectionString = builder.Configuration
    .GetConnectionString("MaintenanceDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:MaintenanceDb is not configured.");

builder.Services.AddDbContext<MaintenanceDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.MapGet("/", () => Results.Ok(new
{
    Application = "AI Maintenance Agent",
    Status = "Running"
}));

app.MapControllers();

app.Run();


public partial class Program { }