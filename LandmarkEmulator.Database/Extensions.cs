using LandmarkEmulator.Database.Configuration;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Concurrent;

namespace LandmarkEmulator.Database
{
    public static class Extensions
    {
        /// <summary>
        /// The database server's version per connection string, asked once. ServerVersion.AutoDetect opens a connection of
        /// its own, outside the pool (Pomelo builds it with Pooling=false), and this runs for every context: each
        /// NewContext() paid a fresh TCP connect and MySQL login before its query (2026-10-18). A failed detection is not
        /// kept, so a database that was down is asked again next time.
        /// </summary>
        private static readonly ConcurrentDictionary<string, ServerVersion> serverVersions = new();

        private static ServerVersion ServerVersionOf(string connectionString)
            => serverVersions.TryGetValue(connectionString, out ServerVersion version) ? version
                : serverVersions.GetOrAdd(connectionString, ServerVersion.AutoDetect(connectionString));

        public static DbContextOptionsBuilder UseConfiguration(this DbContextOptionsBuilder optionsBuilder, IDatabaseConfig databaseConfiguration, DatabaseType databaseType)
        {
            var connectionString = databaseConfiguration.GetConnectionString(databaseType);
            switch (connectionString.Provider)
            {
                case DatabaseProvider.MySql:
                    optionsBuilder.UseMySql(connectionString.ConnectionString, ServerVersionOf(connectionString.ConnectionString), b =>
                    {
                        b.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
                    });
                    break;
                default:
                    // Only reachable with a number that is no DatabaseProvider value ("Provider": 1 in a settings file).
                    throw new NotSupportedException($"Database provider {connectionString.Provider:G} ({databaseType} database) is not "
                        + "supported: only MySql (MySQL or MariaDB) is. Set Database__" + databaseType + "__Provider to MySql.");
            }
            return optionsBuilder;
        }
    }
}
