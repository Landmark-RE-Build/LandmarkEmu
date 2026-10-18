namespace LandmarkEmulator.Database.Configuration
{
    public class DatabaseConnectionString : IConnectionString
    {
        /// <summary>Internal (no .env line): always <see cref="DatabaseProvider.MySql"/>, the only provider (MySQL / MariaDB).</summary>
        public DatabaseProvider Provider { get; set; }
        public string ConnectionString { get; set; }
    }
}
