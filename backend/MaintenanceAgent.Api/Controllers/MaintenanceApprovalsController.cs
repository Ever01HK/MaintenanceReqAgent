using MaintenanceAgent.Api.Data;
using MaintenanceAgent.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MaintenanceAgent.Api.Controllers;

[ApiController]
[Route("api/maintenance-approvals")]
public class MaintenanceApprovalsController : ControllerBase
{
    private readonly MaintenanceDbContext _db;
    private readonly ILogger<MaintenanceApprovalsController> _logger;

    public MaintenanceApprovalsController(
        MaintenanceDbContext db,
        ILogger<MaintenanceApprovalsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // 1. Get pending approval request
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(
        int id,
        CancellationToken ct)
    {
        var approval = await _db.MaintenanceApprovals
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (approval == null)
            return NotFound(new { message = "Approval not found." });

        return Ok(approval);
    }

    // 2. Create actual MaintenanceRequest after approval
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(
        int id,
        CancellationToken ct)
    {
        await using var transaction =
            await _db.Database.BeginTransactionAsync(ct);

        // Change to Approved only when the current status is Pending
        var updated = await _db.MaintenanceApprovals
            .Where(x => x.Id == id && x.Status == "Pending")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Approved")
                .SetProperty(x => x.ReviewedAt, DateTime.UtcNow),
                ct);

        if (updated == 0)
        {
            await transaction.RollbackAsync(ct);

            var current = await _db.MaintenanceApprovals
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (current == null)
                return NotFound(new { message = "Approval not found." });

            _logger.LogWarning(
                "Approval {ApprovalId} cannot be approved. Current status: {Status}",
                id,
                current.Status);

            return Conflict(new
            {
                // Error 409
                message = "This approval has already been processed.",
                current.Status,
                current.MaintenanceRequestId
            });
        }

        var approval = await _db.MaintenanceApprovals
            .AsNoTracking()
            .FirstAsync(x => x.Id == id, ct);

        var request = new MaintenanceRequest
        {
            PropertyId = approval.PropertyId,
            Description = approval.Description,
            Category = approval.Category,
            Priority = approval.Priority,
            Status = "Open",
            CreatedAt = DateTime.UtcNow
        };

        _db.MaintenanceRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        // Save the Request ID that generated in Approval record
        await _db.MaintenanceApprovals
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    x => x.MaintenanceRequestId,
                    request.Id),
                ct);

        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Maintenance approval {ApprovalId} approved. MaintenanceRequest {RequestId} created.",
            id,
            request.Id);

        return Ok(new
        {
            message = "Maintenance request approved and created.",
            approvalId = id,
            maintenanceRequestId = request.Id,
            status = "Approved"
        });
    }

    // 3. Reject approval
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(
        int id,
        CancellationToken ct)
    {
        var updated = await _db.MaintenanceApprovals
            .Where(x => x.Id == id && x.Status == "Pending")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Rejected")
                .SetProperty(x => x.ReviewedAt, DateTime.UtcNow),
                ct);

        if (updated == 0)
        {
            var exists = await _db.MaintenanceApprovals
                .AnyAsync(x => x.Id == id, ct);

            if (!exists)
                return NotFound(new { message = "Approval not found." });

            _logger.LogWarning(
                "Cannot reject approval {ApprovalId}: approval has already been processed.",
                id);

            return Conflict(new
            {
                message = "This approval has already been processed."
            });
        }

        _logger.LogInformation(
            "Maintenance approval {ApprovalId} rejected.",
            id);

        return Ok(new
        {
            approvalId = id,
            status = "Rejected"
        });
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
            return BadRequest(new { message = "Page must be >= 1 and pageSize must be between 1 and 100." });

        if (!string.IsNullOrWhiteSpace(status) &&
            status != "Pending" && status != "Approved" && status != "Rejected")
            return BadRequest(new { message = "Status must be Pending, Approved or Rejected." });

        var query = _db.MaintenanceApprovals.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.PropertyId,
                x.Description,
                x.Category,
                x.Priority,
                x.Status,
                x.MaintenanceRequestId,
                x.CreatedAt,
                x.ReviewedAt
            })
            .ToListAsync(ct);

        return Ok(new { items, totalCount, page, pageSize });
    }

}