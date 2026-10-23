
using MaintenanceAgent.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MaintenanceAgent.Api.Data;


public class MaintenanceDbContext : DbContext
{
    public MaintenanceDbContext(
        DbContextOptions<MaintenanceDbContext> options)
        : base(options)
    {
    }
    public DbSet<Property> Properties => Set<Property>();

    public DbSet<MaintenanceRequest> MaintenanceRequests
        => Set<MaintenanceRequest>();

    public DbSet<MaintenanceApproval> MaintenanceApprovals
        => Set<MaintenanceApproval>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Property>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Address)
                .HasMaxLength(300).IsRequired();
            entity.Property(p => p.TenantName)
                .HasMaxLength(150).IsRequired();
        });

        modelBuilder.Entity<MaintenanceRequest>(entity =>
        {
            entity.HasKey(r => r.Id);

            entity.Property(r => r.Description)
                .HasMaxLength(2000).IsRequired();

            entity.Property(r => r.Category)
                .HasMaxLength(50).IsRequired();

            entity.Property(r => r.Priority)
                .HasMaxLength(20).IsRequired();

            entity.Property(r => r.Status)
                .HasMaxLength(20).IsRequired();

            entity.HasOne(r => r.Property)
                .WithMany(p => p.MaintenanceRequests)
                .HasForeignKey(r => r.PropertyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MaintenanceApproval>(entity =>
        {
            entity.Property(x => x.IdempotencyKey)
                .HasMaxLength(100);

            entity.Property(x => x.RequestHash)
                .HasMaxLength(64);
                
            entity.HasIndex(x => x.IdempotencyKey)
                .IsUnique();
        });
    }
}
