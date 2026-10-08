using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StaySphere.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` (migrations add / script / bundle). Never used at runtime.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("STAYSPHERE_SQL")
            ?? "Server=localhost,1433;Database=StaySphere;User Id=sa;Password=StaySphere_Dev_P@ssw0rd;TrustServerCertificate=True;Encrypt=False";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "platform"))
            .Options;
        return new AppDbContext(options);
    }
}
