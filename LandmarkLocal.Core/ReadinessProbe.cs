using System.Text.Json;

namespace LandmarkLocal;

public enum HostPhase
{
    Stopped,
    Starting,
    Ready,
    Stopping,
    Failed,
}

public sealed record ReadinessSnapshot
{
    public HostPhase Phase { get; init; }
    public string Message { get; init; } = "";
    public bool InfoOk { get; init; }
    public bool WorldListedUp { get; init; }
}

/// <summary>Smoke gate: Auth <c>GET /info</c> and optional world heartbeat in the payload.</summary>
public static class ReadinessProbe
{
    public static async Task<ReadinessSnapshot> ProbeAsync(LocalSettings settings, CancellationToken ct = default)
    {
        string url = $"http://{LocalSettings.Defaults.BindHost}:{settings.AccountPort}/info";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using HttpResponseMessage response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return new ReadinessSnapshot
                {
                    Phase = HostPhase.Starting,
                    Message = $"/info returned {(int)response.StatusCode}",
                    InfoOk = false,
                };
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            bool worldUp = false;
            if (doc.RootElement.TryGetProperty("worlds", out JsonElement worlds) && worlds.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement w in worlds.EnumerateArray())
                {
                    if (w.TryGetProperty("up", out JsonElement up) && up.ValueKind == JsonValueKind.True)
                    {
                        worldUp = true;
                        break;
                    }
                }
            }

            if (worldUp)
            {
                return new ReadinessSnapshot
                {
                    Phase = HostPhase.Ready,
                    Message = "Ready — Auth /info is up and a world is listed as up.",
                    InfoOk = true,
                    WorldListedUp = true,
                };
            }

            return new ReadinessSnapshot
            {
                Phase = HostPhase.Starting,
                Message = "Auth /info is up; waiting for the world to register…",
                InfoOk = true,
                WorldListedUp = false,
            };
        }
        catch (Exception ex)
        {
            return new ReadinessSnapshot
            {
                Phase = HostPhase.Starting,
                Message = "Waiting for Auth… (" + Short(ex.Message) + ")",
                InfoOk = false,
            };
        }
    }

    static string Short(string s) => s.Length <= 80 ? s : s[..80] + "…";
}
