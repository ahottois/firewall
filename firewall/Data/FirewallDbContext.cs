using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Models;

namespace NetworkFirewall.Data;

public class FirewallDbContext : DbContext
{
    public FirewallDbContext(DbContextOptions<FirewallDbContext> options) : base(options)
    {
    }

    public DbSet<NetworkDevice> Devices { get; set; }
    public DbSet<NetworkAlert> Alerts { get; set; }
    public DbSet<TrafficLog> TrafficLogs { get; set; }
    public DbSet<NetworkCamera> Cameras { get; set; }
    public DbSet<ScanSession> ScanSessions { get; set; }
    public DbSet<Agent> Agents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<NetworkDevice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MacAddress).IsUnique();
            entity.Property(e => e.MacAddress).IsRequired().HasMaxLength(17);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.Hostname).HasMaxLength(255);
            entity.Property(e => e.Vendor).HasMaxLength(255);
        });

        modelBuilder.Entity<NetworkAlert>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.IsRead);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
            entity.Property(e => e.SourceMac).HasMaxLength(17);
            entity.Property(e => e.DestinationMac).HasMaxLength(17);
            entity.Property(e => e.SourceIp).HasMaxLength(45);
            entity.Property(e => e.DestinationIp).HasMaxLength(45);
            
            entity.HasOne(e => e.Device)
                  .WithMany(d => d.Alerts)
                  .HasForeignKey(e => e.DeviceId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TrafficLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.SourceMac);
            entity.Property(e => e.SourceMac).IsRequired().HasMaxLength(17);
            entity.Property(e => e.DestinationMac).IsRequired().HasMaxLength(17);
            entity.Property(e => e.SourceIp).HasMaxLength(45);
            entity.Property(e => e.DestinationIp).HasMaxLength(45);
            entity.Property(e => e.Protocol).IsRequired().HasMaxLength(20);
            
            entity.HasOne(e => e.Device)
                  .WithMany(d => d.TrafficLogs)
                  .HasForeignKey(e => e.DeviceId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<NetworkCamera>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.IpAddress, e.Port }).IsUnique();
            entity.Property(e => e.IpAddress).IsRequired().HasMaxLength(45);
            entity.Property(e => e.Manufacturer).HasMaxLength(100);
            entity.Property(e => e.Model).HasMaxLength(100);
            entity.Property(e => e.StreamUrl).HasMaxLength(500);
            entity.Property(e => e.SnapshotUrl).HasMaxLength(500);
            
            entity.HasOne(e => e.Device)
                  .WithMany()
                  .HasForeignKey(e => e.DeviceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScanSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StartTime);
            entity.Property(e => e.ResultSummary).HasMaxLength(1000);
        });

        modelBuilder.Entity<Agent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Hostname);
            entity.HasIndex(e => e.LastSeen);
            entity.Property(e => e.Hostname).IsRequired().HasMaxLength(255);
            entity.Property(e => e.OS).HasMaxLength(50);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
        });
    }
}
