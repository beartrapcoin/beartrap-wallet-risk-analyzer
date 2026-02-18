using Microsoft.EntityFrameworkCore;
using BearTrap.Hackathon.Data.Entities;

namespace BearTrap.Hackathon.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<TokenSnapshotEntity> TokenSnapshots => Set<TokenSnapshotEntity>();
    public DbSet<RiskReportEntity> RiskReports => Set<RiskReportEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TokenSnapshotEntity>()
            .HasIndex(x => x.Address)
            .IsUnique();

        modelBuilder.Entity<RiskReportEntity>()
            .HasIndex(x => x.TokenAddress);
    }
}
