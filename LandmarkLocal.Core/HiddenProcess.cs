using System.Diagnostics;

namespace LandmarkLocal;

/// <summary>Starts a child with no window and tee's stdout/stderr into a log file.</summary>
public sealed class HiddenProcess : IDisposable
{
    readonly Process _process;
    readonly StreamWriter _log;
    readonly object _gate = new();
    bool _disposed;

    HiddenProcess(Process process, StreamWriter log)
    {
        _process = process;
        _log = log;
    }

    public int Id => _process.Id;
    public bool HasExited
    {
        get
        {
            try { return _process.HasExited; }
            catch { return true; }
        }
    }

    public static HiddenProcess Start(string fileName, string arguments, string workingDirectory, string logPath,
        IDictionary<string, string>? environment = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var log = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };
        log.WriteLine($"---- {DateTime.Now:O} start: {fileName} {arguments}");

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        if (environment != null)
        {
            foreach ((string k, string v) in environment)
                psi.Environment[k] = v;
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var wrap = new HiddenProcess(process, log);
        process.OutputDataReceived += (_, e) => wrap.WriteLine(e.Data);
        process.ErrorDataReceived += (_, e) => wrap.WriteLine(e.Data);
        if (!process.Start())
            throw new InvalidOperationException($"Failed to start {fileName}");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return wrap;
    }

    /// <summary>Start a .NET app: prefers .exe; falls back to <c>dotnet path.dll</c>.</summary>
    public static HiddenProcess StartDotNet(string exeOrDll, string workingDirectory, string logPath,
        IDictionary<string, string>? environment = null)
    {
        if (exeOrDll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return Start("dotnet", Quote(exeOrDll), workingDirectory, logPath, environment);
        return Start(exeOrDll, "", workingDirectory, logPath, environment);
    }

    static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;

    void WriteLine(string? line)
    {
        if (line == null)
            return;
        lock (_gate)
        {
            if (!_disposed)
                _log.WriteLine(line);
        }
    }

    public void Stop(TimeSpan grace)
    {
        StopAsync(grace).GetAwaiter().GetResult();
    }

    /// <summary>Graceful close, then kill if still running — does not block the caller thread while waiting.</summary>
    public async Task StopAsync(TimeSpan grace)
    {
        if (HasExited)
            return;
        try
        {
            // Console apps honour Ctrl+C when they own a console; without one, CloseMainWindow is a no-op.
            _process.CloseMainWindow();
        }
        catch
        {
            // ignored
        }

        try
        {
            using var cts = new CancellationTokenSource(grace);
            await _process.WaitForExitAsync(cts.Token);
            return;
        }
        catch (OperationCanceledException)
        {
            // grace elapsed
        }

        try { _process.Kill(entireProcessTree: true); }
        catch { /* already gone */ }

        try
        {
            using var killWait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync(killWait.Token);
        }
        catch (OperationCanceledException)
        {
            // best effort
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _process.Dispose(); } catch { /* ignore */ }
        try { _log.Dispose(); } catch { /* ignore */ }
    }
}
