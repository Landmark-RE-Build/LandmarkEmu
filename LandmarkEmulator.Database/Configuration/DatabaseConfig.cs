using System;

namespace LandmarkEmulator.Database.Configuration
{
    public class DatabaseConfig : IDatabaseConfig
    {
        public DatabaseConnectionString Auth { get; set; }
        public DatabaseConnectionString Character { get; set; }
        public DatabaseConnectionString Zone { get; set; }
        /// <summary>The social service's own database, lmemu_social (LandmarkEmulator.SocialServer only).</summary>
        public DatabaseConnectionString Social { get; set; }

        public IConnectionString GetConnectionString(DatabaseType type)
        {
            return type switch
            {
                DatabaseType.Auth => Auth,
                DatabaseType.Character => Character,
                DatabaseType.Zone => Zone,
                DatabaseType.Social => Social,
                _ => throw new ArgumentException($"Invalid database type: {type:G}", nameof(type))
            };
        }
    }
}
