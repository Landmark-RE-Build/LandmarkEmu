using System.Text.Json;

namespace LandmarkLocal;

public sealed class PidRecord
{
    public int? MariaDb { get; set; }
    public int? Auth { get; set; }
    public int? World { get; set; }
}

public static class PidStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static PidRecord Load(LocalPaths paths)
    {
        if (!File.Exists(paths.PidFile))
            return new PidRecord();
        try
        {
            return JsonSerializer.Deserialize<PidRecord>(File.ReadAllText(paths.PidFile), Options) ?? new PidRecord();
        }
        catch
        {
            return new PidRecord();
        }
    }

    public static void Save(LocalPaths paths, PidRecord record)
    {
        paths.EnsureLayout();
        File.WriteAllText(paths.PidFile, JsonSerializer.Serialize(record, Options));
    }

    public static void Clear(LocalPaths paths)
    {
        if (File.Exists(paths.PidFile))
            File.Delete(paths.PidFile);
    }
}
