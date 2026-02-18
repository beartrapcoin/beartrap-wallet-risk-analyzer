using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BearTrap.Hackathon.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite("Data Source=beartrap_hackathon.db",
            sqliteOptions => sqliteOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
        return new AppDbContext(optionsBuilder.Options);
    }
}