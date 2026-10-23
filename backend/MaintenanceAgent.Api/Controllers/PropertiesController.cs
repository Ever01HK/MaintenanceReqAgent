
using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Dtos;
using MaintenanceAgent.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MaintenanceAgent.Api.Controllers;

[ApiController]
[Route("api/properties")]
public class PropertiesController : ControllerBase
{
    private readonly MaintenanceDbContext _db;

    public PropertiesController(MaintenanceDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var properties = await _db.Properties
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync();

        return Ok(properties.Select(p => new
        {
            p.Id,
            p.Address,
            p.TenantName,
            p.CreatedAt
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreatePropertyRequest request)
    {
        var property = new Property
        {
            Address = request.Address,
            TenantName = request.TenantName
        };

        _db.Properties.Add(property);
        await _db.SaveChangesAsync();

        return Created($"/api/properties/{property.Id}", new
        {
            property.Id,
            property.Address,
            property.TenantName,
            property.CreatedAt
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var property = await _db.Properties
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id,
                p.Address,
                p.TenantName,
                p.CreatedAt
            })
            .FirstOrDefaultAsync();

        return property is null ? NotFound() : Ok(property);
    }
}
