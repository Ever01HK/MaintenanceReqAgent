
using System.ComponentModel.DataAnnotations;

namespace MaintenanceAgent.Api.Dtos;

public class CreatePropertyRequest
{
    [Required]
    [StringLength(300, MinimumLength = 1)]
    public string Address { get; set; } = string.Empty;

    [Required]
    [StringLength(150, MinimumLength = 1)]
    public string TenantName { get; set; } = string.Empty;
}
