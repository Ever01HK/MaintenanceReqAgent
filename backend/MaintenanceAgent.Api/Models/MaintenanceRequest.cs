
namespace MaintenanceAgent.Api.Models;

public class MaintenanceRequest
{
    public int Id { get; set; }
    public int PropertyId { get; set; }

    public Property Property { get; set; } = null!;

    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Other";
    public string Priority { get; set; } = "Normal";
    public string Status { get; set; } = "Open";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
