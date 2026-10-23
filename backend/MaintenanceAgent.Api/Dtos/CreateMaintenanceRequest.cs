
using System.ComponentModel.DataAnnotations;

namespace MaintenanceAgent.Api.Dtos;

public class CreateMaintenanceRequest
{
    [Range(1, int.MaxValue)]
    public int PropertyId { get; set; }

    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Category { get; set; } = "Other";

    [Required]
    [RegularExpression("Low|Normal|High|Emergency")]
    public string Priority { get; set; } = "Normal";
}
