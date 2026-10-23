namespace MaintenanceAgent.Api.Dtos;

public class AgentRequest
{
    public int PropertyId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

}