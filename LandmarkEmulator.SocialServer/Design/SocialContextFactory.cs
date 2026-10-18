using LandmarkEmulator.Database.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LandmarkEmulator.SocialServer.Design
{
    /// <summary>
    /// For "dotnet ef migrations add X --project LandmarkEmulator.Database.Social --startup-project
    /// LandmarkEmulator.SocialServer": a context with a fixed server version (the compose file's mariadb:11.4), so making a
    /// migration needs no running database.
    /// </summary>
    public class SocialContextFactory : IDesignTimeDbContextFactory<SocialContext>
    {
        public SocialContext CreateDbContext(string[] args)
            => new(new DbContextOptionsBuilder<SocialContext>()
                .UseMySql("server=127.0.0.1;database=lmemu_social", new MariaDbServerVersion(new System.Version(11, 4)))
                .Options);
    }
}
