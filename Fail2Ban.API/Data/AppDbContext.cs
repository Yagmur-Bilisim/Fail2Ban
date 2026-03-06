using Microsoft.EntityFrameworkCore;
using Fail2Ban.API.Models;

namespace Fail2Ban.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<BanRecord> BanRecords { get; set; }
    public DbSet<LogPointer> LogPointers { get; set; }
    public DbSet<WhitelistedIp> WhitelistedIps { get; set; }
    public DbSet<FailedAttempt> FailedAttempts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<BanRecord>()
            .HasIndex(b => b.IpAddress);
            
        modelBuilder.Entity<BanRecord>()
            .HasIndex(b => b.IsActive);
            
        modelBuilder.Entity<WhitelistedIp>()
            .HasIndex(w => w.IpAddress)
            .IsUnique();
            
        modelBuilder.Entity<FailedAttempt>()
            .HasIndex(f => new { f.IpAddress, f.Source })
            .IsUnique();
    }
}
