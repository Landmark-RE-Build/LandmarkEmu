namespace LandmarkEmulator.Database.Configuration
{
    /// <summary>
    /// The database server kind of a connection string (Database__*__Provider). Only MySql (MySQL or MariaDB, through
    /// Pomelo) exists: every migration and model snapshot is written for it, and the Docker stack runs MariaDB.
    /// "MicrosoftSqlServer" was listed here until 2026-10-18 but refused at start (Extensions.UseConfiguration); a
    /// settings file that still names it now fails while the settings are read, naming the key and the value.
    /// </summary>
    public enum DatabaseProvider
    {
        MySql
    }
}
