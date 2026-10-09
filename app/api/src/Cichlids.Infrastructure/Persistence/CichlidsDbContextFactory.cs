using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cichlids.Infrastructure.Persistence;

/// <summary>
/// Builds a <see cref="CichlidsDbContext"/> for the dotnet-ef design-time tooling (creating and
/// applying migrations). The connection string here is never opened; the tooling only needs a
/// syntactically valid one to build the model.
/// </summary>
public class CichlidsDbContextFactory : IDesignTimeDbContextFactory<CichlidsDbContext>
{
    public CichlidsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>();
        optionsBuilder
            .UseNpgsql("Host=localhost;Database=cichlids;Username=cichlids;Password=cichlids")
            .UseSnakeCaseNamingConvention();

        return new CichlidsDbContext(optionsBuilder.Options);
    }
}
