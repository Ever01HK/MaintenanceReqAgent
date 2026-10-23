
using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Dtos;
using MaintenanceAgent.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MaintenanceAgent.Api.Controllers;

[ApiController]
[Route("api/maintenance-requests")]
public class MaintenanceRequestsController : ControllerBase
{
    private readonly MaintenanceDbContext _db;

    public MaintenanceRequestsController(MaintenanceDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var requests = await _db.MaintenanceRequests
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.PropertyId,
                r.Description,
                r.Category,
                r.Priority,
                r.Status,
                r.CreatedAt
            })
            .ToListAsync();

        return Ok(requests);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var request = await _db.MaintenanceRequests
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new
            {
                r.Id,
                r.PropertyId,
                r.Description,
                r.Category,
                r.Priority,
                r.Status,
                r.CreatedAt
            })
            .FirstOrDefaultAsync();

        return request is null ? NotFound() : Ok(request);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateMaintenanceRequest request)
    {
        var propertyExists = await _db.Properties
            .AnyAsync(p => p.Id == request.PropertyId);

        if (!propertyExists)
        {
            return BadRequest(new
            {
                error = "Property does not exist."
            });
        }

        var maintenance = new MaintenanceRequest
        {
            PropertyId = request.PropertyId,
            Description = request.Description,
            Category = request.Category,
            Priority = request.Priority,
            Status = "Open"
        };

        _db.MaintenanceRequests.Add(maintenance);
        await _db.SaveChangesAsync();

        return Created(
            $"/api/maintenance-requests/{maintenance.Id}",
            new
            {
                maintenance.Id,
                maintenance.PropertyId,
                maintenance.Description,
                maintenance.Category,
                maintenance.Priority,
                maintenance.Status,
                maintenance.CreatedAt
            });
    }
}
