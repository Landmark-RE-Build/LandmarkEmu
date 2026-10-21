namespace LandmarkLocal;

/// <summary>CLI: start | stop | status | setup | play.</summary>
public static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        string command = args[0].ToLowerInvariant();
        LocalPaths paths = LocalPaths.Resolve();
        LocalSettings settings = LocalSettings.LoadOrCreate(paths);
        var progress = new Progress<string>(m =>
        {
            if (!m.StartsWith(LocalOrchestrator.StatusPrefix, StringComparison.Ordinal))
                Console.WriteLine(m);
        });

        try
        {
            switch (command)
            {
                case "setup":
                    return await SetupAsync(paths, settings, args.Skip(1).ToArray());
                case "start":
                {
                    ApplyArgOverrides(settings, args.Skip(1).ToArray());
                    settings.Save(paths);
                    using var host = new LocalOrchestrator(paths, settings);
                    await host.StartAsync(progress);
                    Console.WriteLine(host.LastMessage);
                    Console.WriteLine("Servers are running. Use: LandmarkLocal play   then   LandmarkLocal stop");
                    return 0;
                }
                case "stop":
                {
                    using var host = new LocalOrchestrator(paths, settings);
                    await host.StopAsync(progress);
                    Console.WriteLine("Stopped.");
                    return 0;
                }
                case "status":
                case "ready":
                {
                    using var host = new LocalOrchestrator(paths, settings);
                    ReadinessSnapshot snap = await host.StatusAsync();
                    Console.WriteLine(snap.Phase + ": " + snap.Message);
                    return snap.Phase == HostPhase.Ready ? 0 : 1;
                }
                case "play":
                    return await PlayAsync(paths, settings, args.Skip(1).ToArray(), progress);
                case "help":
                case "-h":
                case "--help":
                    PrintHelp();
                    return 0;
                default:
                    Console.Error.WriteLine("Unknown command: " + command);
                    PrintHelp();
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static async Task<int> SetupAsync(LocalPaths paths, LocalSettings settings, string[] args)
    {
        ApplyArgOverrides(settings, args);
        if (string.IsNullOrWhiteSpace(settings.LandmarkFolder))
            settings.LandmarkFolder = LocalSettings.TryFindSteamLandmark() ?? "";

        if (string.IsNullOrWhiteSpace(settings.LandmarkFolder) || !LocalSettings.LooksLikeLandmarkFolder(settings.LandmarkFolder))
        {
            Console.Write("Landmark folder (contains Landmark64.exe): ");
            settings.LandmarkFolder = (Console.ReadLine() ?? "").Trim().Trim('"');
        }
        if (!LocalSettings.LooksLikeLandmarkFolder(settings.LandmarkFolder))
        {
            Console.Error.WriteLine("That folder is not a Landmark install.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.AdminAccountName))
        {
            Console.Write("Account name (also your admin on this world): ");
            settings.AdminAccountName = (Console.ReadLine() ?? "").Trim();
        }
        if (!AccountRules.IsValidAccountName(settings.AdminAccountName))
        {
            Console.Error.WriteLine("Account name: 3 to 32 letters, digits or _.");
            return 1;
        }

        settings.EnsureCredentials();
        settings.Save(paths);
        Console.WriteLine("Saved " + paths.SettingsFile);

        var progress = new Progress<string>(m => Console.WriteLine(m));
        GameDataSetupResult gamedata = await GameDataSetup.RunAsync(paths, settings, progress);
        Console.WriteLine(gamedata.Message);
        if (!gamedata.Ok)
            return 1;

        LaunchPadInstallResult lp = LaunchPadInstaller.Install(paths, settings);
        Console.WriteLine(lp.Message);
        Console.WriteLine("Next: LandmarkLocal start → wait for Ready → LandmarkLocal play");
        return lp.Ok ? 0 : 1;
    }

    static async Task<int> PlayAsync(LocalPaths paths, LocalSettings settings, string[] args,
        IProgress<string> progress)
    {
        ApplyArgOverrides(settings, args);
        string? password = null;
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--password" || args[i] == "-p") && i + 1 < args.Length)
                password = args[++i];
        }

        if (!settings.HasRememberedLogin() && string.IsNullOrEmpty(password))
        {
            Console.Write("Password (not stored; SRP key is remembered after Play): ");
            password = ReadPassword();
            Console.WriteLine();
        }

        settings.Save(paths);
        LocalPlayResult result = await LocalPlay.PlayAsync(paths, settings, password, progress);
        Console.WriteLine(result.Message);
        return result.Ok ? 0 : 1;
    }

    static string ReadPassword()
    {
        var chars = new List<char>();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (chars.Count > 0)
                    chars.RemoveAt(chars.Count - 1);
                continue;
            }
            if (!char.IsControl(key.KeyChar))
                chars.Add(key.KeyChar);
        }
        return new string(chars.ToArray());
    }

    static void ApplyArgOverrides(LocalSettings settings, string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if ((a == "--landmark" || a == "-l") && i + 1 < args.Length)
                settings.LandmarkFolder = args[++i].Trim().Trim('"');
            else if ((a == "--admin" || a == "-a" || a == "--account") && i + 1 < args.Length)
            {
                string name = args[++i].Trim();
                if (!string.Equals(settings.AdminAccountName, name, StringComparison.OrdinalIgnoreCase))
                    settings.ForgetLogin();
                settings.AdminAccountName = name;
            }
        }
    }

    static void PrintHelp()
    {
        Console.WriteLine("""
            LandmarkLocal — play LandmarkEmu alone on this PC

            Usual path: open the window (no args), set Landmark folder + account name,
            Start → Ready → Play (creates your admin account the first time).
            Full steps: docs/landmark-local.md

              LandmarkLocal         Open the window
              LandmarkLocal setup   Ask for folder + account name
              LandmarkLocal start   Start until Ready
              LandmarkLocal play    Register/login, patch if needed, start Landmark
              LandmarkLocal stop    Stop
              LandmarkLocal status  Ready?

              --landmark <path>   Folder with Landmark64.exe
              --admin <name>      Account name (also admin)
              --password <pass>   For play (first time / when not remembered)
            """);
    }
}
