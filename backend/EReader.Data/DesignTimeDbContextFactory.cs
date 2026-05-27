using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EReader.Data;

/// <summary>
/// Used by `dotnet ef` design-time tooling (migrations add / update / scaffold).
/// Bypasses Program.cs entirely so auth/Redis config aren't required just to
/// generate a migration. The runtime DI registration in Program.cs is unchanged.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EReaderDbContext>
{
    public EReaderDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? ReadEnvFile("DATABASE_URL")
            ?? "Host=localhost;Port=5432;Database=ereader;Username=ereader;Password=ereader";

        var options = new DbContextOptionsBuilder<EReaderDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new EReaderDbContext(options);
    }

    private static string? ReadEnvFile(string key)
    {
        // Look upward for a .env at the repo root — mirrors what DotNetEnv does
        // in the API project but avoids dragging that dep into EReader.Data
        // for design-time-only use.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var envPath = Path.Combine(dir.FullName, ".env");
            if (File.Exists(envPath))
            {
                foreach (var line in File.ReadAllLines(envPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                    var eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;
                    if (trimmed[..eq].Trim() == key)
                    {
                        return trimmed[(eq + 1)..].Trim().Trim('"');
                    }
                }
                return null;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
