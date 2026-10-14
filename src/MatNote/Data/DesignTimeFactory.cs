using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MatNote.Data;

/// <summary>Lets <c>dotnet ef migrations add</c> create the context without starting the app (no database needs to be running).</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=matnote;Username=matnote;Password=design-time").Options;
        return new AppDbContext(options);
    }
}
