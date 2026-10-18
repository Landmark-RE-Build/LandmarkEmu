namespace LandmarkEmulator.Database.Configuration
{
    public enum DatabaseType
    {
        Auth,
        Character,
        Zone,
        /// <summary>The shared social service's own database, lmemu_social (trace/architecture/social-service.md §5.2).</summary>
        Social
    }
}
