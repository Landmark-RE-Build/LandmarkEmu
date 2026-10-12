using Microsoft.EntityFrameworkCore;

namespace LandmarkEmulator.WorldServer.Storage
{
    /// <summary>
    /// Partial updates without loading the row: attach a stub with the key, set the fields, then mark them modified.
    /// Without the marking, EF Core writes only fields whose value differs from the stub's defaults, so e.g. moving an
    /// item to the bag (container 0, slot 0), back to landscape 406 or a site time back to -1 was silently not saved.
    /// </summary>
    public static class DbExtensions
    {
        public static void MarkModified(this DbContext context, object entity, params string[] properties)
        {
            var entry = context.Entry(entity);
            foreach (string property in properties)
                entry.Property(property).IsModified = true;
        }
    }
}
