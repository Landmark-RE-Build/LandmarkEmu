using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace LandmarkLocal;

/// <summary>First-run download + init of portable MariaDB; start/stop for solo loopback.</summary>
public sealed class MariaDbHost : IDisposable
{
    readonly LocalPaths _paths;
    readonly LocalSettings _settings;
    HiddenProcess? _process;

    public MariaDbHost(LocalPaths paths, LocalSettings settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public bool IsRunning => _process is { HasExited: false };
    public int? ProcessId => _process is { HasExited: false } ? _process.Id : null;

    public string BinDir => Path.Combine(_paths.MariaDbDir, MariaDbPin.UnpackFolderName, "bin");
    public string Mysqld => Path.Combine(BinDir, "mysqld.exe");
    public string Mysql => Path.Combine(BinDir, "mysql.exe");
    public string MysqlInstallDb => Path.Combine(BinDir, "mysql_install_db.exe");
    public string MyIni => Path.Combine(_paths.MariaDbDir, "my.ini");

    public async Task EnsureInstalledAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (File.Exists(Mysqld))
        {
            progress?.Report("MariaDB already installed.");
            WriteMyIni();
            return;
        }

        string zip = await DownloadCache.EnsureAsync(_paths, MariaDbPin.DownloadUrl, MariaDbPin.FileName, MariaDbPin.Sha256,
            progress, ct);
        Directory.CreateDirectory(_paths.MariaDbDir);
        await ZipProgress.ExtractToDirectoryAsync(
            zip, _paths.MariaDbDir, "Unpacking MariaDB (once; can take a few minutes)", progress, ct);
        if (!File.Exists(Mysqld))
            throw new InvalidOperationException($"MariaDB unpack did not produce {Mysqld}");
        WriteMyIni();
    }

    public async Task EnsureInitializedAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await EnsureInstalledAsync(progress, ct);
        string marker = Path.Combine(_paths.MariaDbDataDir, "landmarklocal-init.done");
        if (File.Exists(marker) && Directory.Exists(Path.Combine(_paths.MariaDbDataDir, "mysql")))
        {
            progress?.Report("MariaDB data already initialised.");
            return;
        }

        // Wipe a half-finished data dir (e.g. after a failed first attempt).
        if (Directory.Exists(_paths.MariaDbDataDir))
        {
            progress?.Report("Clearing incomplete MariaDB data…");
            Directory.Delete(_paths.MariaDbDataDir, recursive: true);
        }
        Directory.CreateDirectory(_paths.MariaDbDataDir);
        WriteMyIni();
        progress?.Report("Initialising MariaDB data directory…");
        RunInstallDb();
        progress?.Report("Starting MariaDB to create LandmarkEmu databases…");
        await StartCoreAsync(progress);
        await WaitReadyAsync(TimeSpan.FromMinutes(2), ct);
        ApplySql(BootstrapSql(_settings), asRootWithPassword: true);
        File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        progress?.Report("MariaDB ready.");
    }

    public async Task StartAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (IsRunning)
            return;
        await EnsureInstalledAsync(progress, ct);
        WriteMyIni();
        if (!Directory.Exists(Path.Combine(_paths.MariaDbDataDir, "mysql")))
            await EnsureInitializedAsync(progress, ct);
        if (IsRunning)
            return;
        await StartCoreAsync(progress);
    }

    Task StartCoreAsync(IProgress<string>? progress)
    {
        progress?.Report("Starting MariaDB…");
        string log = Path.Combine(_paths.LogsDir, "mariadb.log");
        _process = HiddenProcess.Start(Mysqld,
            $"--defaults-file=\"{MyIni}\" --console",
            BinDir, log);
        return Task.CompletedTask;
    }

    public void Stop(TimeSpan? grace = null)
    {
        StopAsync(grace).GetAwaiter().GetResult();
    }

    public async Task StopAsync(TimeSpan? grace = null)
    {
        if (_process == null)
            return;
        await _process.StopAsync(grace ?? TimeSpan.FromSeconds(20));
        _process.Dispose();
        _process = null;
    }

    public async Task WaitReadyAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await CanConnectAsync(ct))
                return;
            await Task.Delay(500, ct);
        }
        throw new TimeoutException($"MariaDB did not accept connections on 127.0.0.1:{_settings.DbPort}.");
    }

    async Task<bool> CanConnectAsync(CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(LocalSettings.Defaults.BindHost, _settings.DbPort);
            var finished = await Task.WhenAny(connect, Task.Delay(1000, ct));
            if (finished != connect)
                return false;
            await connect;
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    void WriteMyIni()
    {
        Directory.CreateDirectory(_paths.MariaDbDir);
        Directory.CreateDirectory(_paths.MariaDbDataDir);
        string basedir = Path.Combine(_paths.MariaDbDir, MariaDbPin.UnpackFolderName).Replace('\\', '/');
        string datadir = _paths.MariaDbDataDir.Replace('\\', '/');
        string text = $"""
            [mysqld]
            basedir={basedir}
            datadir={datadir}
            port={_settings.DbPort}
            bind-address={LocalSettings.Defaults.BindHost}
            character-set-server=utf8mb4
            collation-server=utf8mb4_unicode_ci
            innodb_buffer_pool_size=256M
            skip-networking=0

            [client]
            port={_settings.DbPort}
            host={LocalSettings.Defaults.BindHost}
            """;
        File.WriteAllText(MyIni, text);
    }

    /// <summary>Windows MariaDB zip uses mysql_install_db.exe (mysqld --initialize-insecure is MySQL-only).</summary>
    void RunInstallDb()
    {
        if (!File.Exists(MysqlInstallDb))
            throw new InvalidOperationException("mysql_install_db.exe missing from the MariaDB zip.");

        var psi = new ProcessStartInfo
        {
            FileName = MysqlInstallDb,
            Arguments = $"--datadir=\"{_paths.MariaDbDataDir}\" --password=\"{_settings.DbRootPassword}\" --port={_settings.DbPort}",
            WorkingDirectory = BinDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("mysql_install_db failed to start.");
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Directory.CreateDirectory(_paths.LogsDir);
        File.AppendAllText(Path.Combine(_paths.LogsDir, "mariadb-init.log"),
            $"---- {DateTime.Now:O}\nmysql_install_db\n{stdout}\n{stderr}\nexit={p.ExitCode}\n");
        if (p.ExitCode != 0 || !Directory.Exists(Path.Combine(_paths.MariaDbDataDir, "mysql")))
            throw new InvalidOperationException($"mysql_install_db failed (exit {p.ExitCode}). See logs\\mariadb-init.log");
    }

    /// <summary>
    /// Ensures <paramref name="adminEntry"/> (e.g. <c>account:Alice</c>) is in <c>lmemu_auth.admin</c>.
    /// Live worlds pick this up within about a minute without a restart.
    /// </summary>
    public void EnsureAdminEntry(string adminEntry)
    {
        if (string.IsNullOrWhiteSpace(adminEntry))
            return;
        if (!File.Exists(Mysql))
            throw new InvalidOperationException("MariaDB client not found (Start the servers once first).");
        string escaped = adminEntry.Trim().Replace("'", "''", StringComparison.Ordinal);
        ApplySql(
            $"INSERT IGNORE INTO lmemu_auth.admin (entry) VALUES ('{escaped}');",
            asRootWithPassword: true);
    }

    void ApplySql(string sql, bool asRootWithPassword)
    {
        string args = asRootWithPassword
            ? $"-u root -p{_settings.DbRootPassword} --protocol=tcp -h {LocalSettings.Defaults.BindHost} -P {_settings.DbPort}"
            : $"-u root --protocol=tcp -h {LocalSettings.Defaults.BindHost} -P {_settings.DbPort}";
        var psi = new ProcessStartInfo
        {
            FileName = Mysql,
            Arguments = args,
            WorkingDirectory = BinDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("mysql client failed to start.");
        p.StandardInput.Write(sql);
        p.StandardInput.Close();
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        File.AppendAllText(Path.Combine(_paths.LogsDir, "mariadb-init.log"),
            $"---- SQL {DateTime.Now:O}\n{stdout}\n{stderr}\nexit={p.ExitCode}\n");
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"MariaDB bootstrap SQL failed. See logs\\mariadb-init.log");
    }

    /// <summary>Same grants as docker/db-init/01-databases.sql; root password is set by mysql_install_db.</summary>
    public static string BootstrapSql(LocalSettings settings)
    {
        string pass = settings.DbPassword.Replace("'", "''");
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE USER IF NOT EXISTS 'landmarkemu'@'%' IDENTIFIED BY '{pass}';");
        sb.AppendLine($"CREATE USER IF NOT EXISTS 'landmarkemu'@'localhost' IDENTIFIED BY '{pass}';");
        sb.AppendLine($"CREATE USER IF NOT EXISTS 'landmarkemu'@'127.0.0.1' IDENTIFIED BY '{pass}';");
        sb.AppendLine("CREATE DATABASE IF NOT EXISTS lmemu_auth CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        sb.AppendLine("CREATE DATABASE IF NOT EXISTS lmemu_character CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        sb.AppendLine("GRANT ALL PRIVILEGES ON lmemu_auth.* TO 'landmarkemu'@'%';");
        sb.AppendLine("GRANT ALL PRIVILEGES ON lmemu_character.* TO 'landmarkemu'@'%';");
        sb.AppendLine("GRANT ALL PRIVILEGES ON `lmemu\\_world\\_%`.* TO 'landmarkemu'@'%';");
        sb.AppendLine("GRANT ALL PRIVILEGES ON lmemu_social.* TO 'landmarkemu'@'%';");
        sb.AppendLine("GRANT ALL PRIVILEGES ON `lmemu\\_wc\\_%`.* TO 'landmarkemu'@'%';");
        sb.AppendLine("GRANT ALL PRIVILEGES ON lmemu_commerce.* TO 'landmarkemu'@'%';");
        sb.AppendLine("GRANT ALL PRIVILEGES ON lmemu_auth.* TO 'landmarkemu'@'localhost';");
        sb.AppendLine("GRANT ALL PRIVILEGES ON lmemu_character.* TO 'landmarkemu'@'localhost';");
        sb.AppendLine("GRANT ALL PRIVILEGES ON `lmemu\\_world\\_%`.* TO 'landmarkemu'@'localhost';");
        sb.AppendLine("GRANT ALL PRIVILEGES ON lmemu_auth.* TO 'landmarkemu'@'127.0.0.1';");
        sb.AppendLine("GRANT ALL PRIVILEGES ON lmemu_character.* TO 'landmarkemu'@'127.0.0.1';");
        sb.AppendLine("GRANT ALL PRIVILEGES ON `lmemu\\_world\\_%`.* TO 'landmarkemu'@'127.0.0.1';");
        sb.AppendLine("FLUSH PRIVILEGES;");
        return sb.ToString();
    }

    /// <summary>Does not stop mysqld (same reason as <see cref="LocalOrchestrator.Dispose"/>).</summary>
    public void Dispose()
    {
    }
}
