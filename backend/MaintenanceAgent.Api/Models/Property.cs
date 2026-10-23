
namespace MaintenanceAgent.Api.Models;
public class Property
{
    public int Id { get; set; }
    public string Address { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<MaintenanceRequest> MaintenanceRequests { get; set; } = new();
}
