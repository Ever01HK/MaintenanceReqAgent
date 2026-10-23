namespace MaintenanceAgent.Api.Models;

public class MaintenanceApproval
{
    public int Id { get; set; }

    public int PropertyId { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Other";

    public string Priority { get; set; } = "Normal";

    public string Status { get; set; } = "Pending";

    public int? MaintenanceRequestId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReviewedAt { get; set; }

    // For Idempotency
    public string? IdempotencyKey {get; set;}
    // For checking origin request
    public string? RequestHash {get; set;}
}
