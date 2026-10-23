using System.Diagnostics;

namespace LandmarkLocal;

/// <summary>Starts and stops MariaDB + Auth + World for forever-local solo play.</summary>
public sealed class LocalOrchestrator : IDisposable
{
    readonly LocalPaths _paths;
    LocalSettings _settings;
    MariaDbHost? _db;
    HiddenProcess? _auth;
    HiddenProcess? _world;

    public LocalOrchestrator(LocalPaths paths, LocalSettings settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public LocalPaths Paths => _paths;
    public LocalSettings Settings => _settings;
    public HostPhase Phase { get; private set; } = HostPhase.Stopped;
    public string LastMessage { get; private set; } = "Stopped.";

    public void UpdateSettings(LocalSettings settings)
    {
        _settings = settings;
        _settings.EnsureCredentials();
        _settings.Save(_paths);
    }

    public async Task StartAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (Phase is HostPhase.Starting or HostPhase.Ready)
        {
            progress?.Report("Already starting or ready.");
            return;
        }

        Phase = HostPhase.Starting;
        try
        {
            _settings.EnsureCredentials();
            _settings.Save(_paths);
            _paths.EnsureLayout();

            if (_settings.NeedsFirstRun)
                throw new InvalidOperationException("First-run setup needed: Landmark folder and admin account name.");

            if (!LocalSettings.LooksLikeLandmarkFolder(_settings.LandmarkFolder))
                throw new InvalidOperationException("Landmark folder is invalid (need Landmark64.exe and Resources).");

            List<PortConflict> conflicts = PortChecker.FindConflicts(PortChecker.RequiredPorts(_settings));
            if (conflicts.Count > 0)
            {
                string detail = string.Join("; ", conflicts.Select(c => $"{c.Name} ({c.Protocol} {c.Port}): {c.Detail}"));
                throw new InvalidOperationException(
                    "A required port is already in use — often leftover LandmarkLocal servers from a previous run.\n" +
                    "Click Stop in LandmarkLocal (or close other LandmarkEmu/Docker copies), then Start again.\n" +
                    detail);
            }

            progress?.Report("Preparing game data…");
            progress?.Report(StatusPrefix + "Preparing game data…");
            GameDataSetupResult gamedata = await GameDataSetup.RunAsync(_paths, _settings, progress, ct);
            if (!gamedata.Ok)
                throw new InvalidOperationException(gamedata.Message);
            progress?.Report(gamedata.Message);

            progress?.Report("Writing server configs…");
            ConfigWriter.WriteAll(_paths, _settings);

            string? authExe = _paths.FindAuthExe();
            string? worldExe = _paths.FindWorldExe();
            if (authExe == null || worldExe == null)
            {
                throw new InvalidOperationException(
                    "Auth or World server binaries were not found. Publish them into servers\\AuthServer and servers\\WorldServer "
                    + "(see docs/landmark-local.md), or build the solution in this repo first.");
            }

            string authDir = Path.GetDirectoryName(authExe)!;
            string worldDir = Path.GetDirectoryName(worldExe)!;
            // Dev builds use bin\…; copy generated JSON beside those exes when servers\ is empty.
            EnsureConfigBeside(authExe, _paths.AuthServerJson, "AuthServer.json");
            EnsureConfigBeside(worldExe, _paths.WorldServerJson, "WorldServer.json");
            WorldDataLinks.Ensure(_paths, worldDir);

            LaunchPadInstallResult lp = LaunchPadInstaller.Install(_paths, _settings);
            progress?.Report(lp.Ok ? lp.Message : "LaunchPad: " + lp.Message);

            progress?.Report("Setting up MariaDB…");
            progress?.Report(StatusPrefix + "Setting up MariaDB…");
            _db = new MariaDbHost(_paths, _settings);
            await _db.EnsureInitializedAsync(progress, ct);
            if (!_db.IsRunning)
                await _db.StartAsync(progress, ct);
            await _db.WaitReadyAsync(TimeSpan.FromMinutes(2), ct);

            progress?.Report("Starting Auth server…");
            progress?.Report(StatusPrefix + "Starting Auth server…");
            _auth = HiddenProcess.StartDotNet(authExe, authDir, Path.Combine(_paths.LogsDir, "auth.log"));
            await WaitForAsync(async () => (await ReadinessProbe.ProbeAsync(_settings, ct)).InfoOk,
                TimeSpan.FromMinutes(3), "Auth", progress, StatusTick(progress), ct);

            progress?.Report("Starting World server…");
            progress?.Report(StatusPrefix + "Starting World server…");
            _world = HiddenProcess.StartDotNet(worldExe, worldDir, Path.Combine(_paths.LogsDir, "world.log"));

            var worldStarted = DateTime.UtcNow;
            var waits = new ProgressThrottle(progress, TimeSpan.FromSeconds(8), StatusTick(progress));
            var deadline = worldStarted + TimeSpan.FromMinutes(10);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                ReadinessSnapshot snap = await ReadinessProbe.ProbeAsync(_settings, ct);
                if (snap.Phase == HostPhase.Ready)
                {
                    Phase = HostPhase.Ready;
                    string took = ProgressThrottle.FormatElapsed(DateTime.UtcNow - worldStarted);
                    LastMessage = $"Ready in {took} — press Play to create your account (first time) and start Landmark.";
                    progress?.Report(LastMessage);
                    progress?.Report(StatusPrefix + "Ready");
                    SavePids();
                    return;
                }

                waits.ReportWaiting(
                    "Waiting for the world to register…",
                    worldStarted,
                    firstTip: "The world loads tables and landscapes first; several minutes is normal on a slow disk.");

                if (_auth.HasExited)
                    throw new InvalidOperationException("Auth server exited early. See logs\\auth.log");
                if (_world.HasExited)
                    throw new InvalidOperationException("World server exited early. See logs\\world.log");
                await Task.Delay(2000, ct);
            }

            throw new TimeoutException("Timed out waiting for the world to become ready. See logs\\world.log");
        }
        catch (Exception ex)
        {
            Phase = HostPhase.Failed;
            LastMessage = ex.Message;
            progress?.Report("Failed: " + ex.Message);
            try { await StopAsync(progress); } catch { /* best effort */ }
            throw;
        }
    }

    public async Task StopAsync(IProgress<string>? progress = null)
    {
        Phase = HostPhase.Stopping;
        ReportStop(progress, "Stopping world — this can take up to a minute…");
        if (_world != null)
        {
            await _world.StopAsync(TimeSpan.FromSeconds(90));
            _world.Dispose();
            _world = null;
        }

        ReportStop(progress, "Stopping auth…");
        if (_auth != null)
        {
            await _auth.StopAsync(TimeSpan.FromSeconds(20));
            _auth.Dispose();
            _auth = null;
        }

        ReportStop(progress, "Stopping MariaDB…");
        if (_db != null)
        {
            await _db.StopAsync(TimeSpan.FromSeconds(20));
            _db.Dispose();
            _db = null;
        }

        // PID file from a prior Start, plus by-name fallback (e.g. Auth was restarted by hand).
        ReportStop(progress, "Cleaning up leftover processes…");
        await Task.Run(() =>
        {
            KillRecorded();
            KillByImageName("LandmarkEmulator.WorldServer");
            KillByImageName("LandmarkEmulator.AuthServer");
            KillMysqldUnderOurData();
        });
        PidStore.Clear(_paths);
        Phase = HostPhase.Stopped;
        LastMessage = "Stopped.";
        progress?.Report("Stopped.");
        progress?.Report(StatusPrefix + "Stopped");
    }

    static void ReportStop(IProgress<string>? progress, string line)
    {
        progress?.Report(line);
        progress?.Report(StatusPrefix + line);
    }

    public async Task<ReadinessSnapshot> StatusAsync(CancellationToken ct = default)
    {
        if (Phase is HostPhase.Stopping)
            return new ReadinessSnapshot { Phase = HostPhase.Stopping, Message = LastMessage };

        if (Phase == HostPhase.Ready || Phase == HostPhase.Starting)
        {
            ReadinessSnapshot snap = await ReadinessProbe.ProbeAsync(_settings, ct);
            if (snap.Phase == HostPhase.Ready)
            {
                Phase = HostPhase.Ready;
                LastMessage = snap.Message;
            }
            return snap with { Message = Phase == HostPhase.Ready ? LastMessage : snap.Message };
        }

        ReadinessSnapshot live = await ReadinessProbe.ProbeAsync(_settings, ct);
        if (live.InfoOk)
        {
            return live with
            {
                Message = "Servers appear to be up (started outside this session).\n" + live.Message,
            };
        }

        return new ReadinessSnapshot { Phase = HostPhase.Stopped, Message = LastMessage };
    }

    void SavePids()
    {
        PidStore.Save(_paths, new PidRecord
        {
            MariaDb = _db?.ProcessId,
            Auth = _auth is { HasExited: false } ? _auth.Id : null,
            World = _world is { HasExited: false } ? _world.Id : null,
        });
    }

    void KillRecorded()
    {
        PidRecord record = PidStore.Load(_paths);
        foreach (int? id in new int?[] { record.World, record.Auth, record.MariaDb })
        {
            if (id is not int pid)
                continue;
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
            }
            catch
            {
                // already gone
            }
        }
    }

    static void KillByImageName(string processName)
    {
        foreach (Process p in Process.GetProcessesByName(processName))
        {
            try
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(10_000);
            }
            catch
            {
                // already gone
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    void KillMysqldUnderOurData()
    {
        string ourBin = Path.GetFullPath(_paths.MariaDbDir);
        foreach (Process p in Process.GetProcessesByName("mysqld"))
        {
            try
            {
                string? exe = p.MainModule?.FileName;
                if (exe != null && exe.StartsWith(ourBin, StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(10_000);
                }
            }
            catch
            {
                // already gone / access denied
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    static void EnsureConfigBeside(string exeOrDll, string generatedJson, string fileName)
    {
        string dir = Path.GetDirectoryName(exeOrDll)!;
        string dest = Path.Combine(dir, fileName);
        if (string.Equals(Path.GetFullPath(generatedJson), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            return; // already written next to the published server
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.Copy(generatedJson, dest, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(200);
            }
        }
        File.Copy(generatedJson, dest, overwrite: true);
    }

    /// <summary>UI banner lines use this prefix; the window strips it and does not append to the activity log.</summary>
    public const string StatusPrefix = "status:";

    static Action<string> StatusTick(IProgress<string>? progress)
        => text => progress?.Report(StatusPrefix + text);

    static async Task WaitForAsync(Func<Task<bool>> predicate, TimeSpan timeout, string label,
        IProgress<string>? progress, Action<string>? status, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var waits = new ProgressThrottle(progress, TimeSpan.FromSeconds(8), status);
        string waiting = $"Waiting for {label}…";
        var deadline = started + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await predicate())
            {
                progress?.Report($"{label} is up.");
                status?.Invoke($"{label} is up");
                return;
            }
            waits.ReportWaiting(waiting, started);
            await Task.Delay(1000, ct);
        }
        throw new TimeoutException($"Timed out waiting for {label}.");
    }

    /// <summary>
    /// Releases log handles only. Does <b>not</b> stop servers — otherwise CLI <c>start</c> would kill MariaDB on exit.
    /// Use <see cref="StopAsync"/> to shut down.
    /// </summary>
    public void Dispose()
    {
        // Intentionally leave Auth/World/MariaDB running.
    }
}
