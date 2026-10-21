using System.Runtime.InteropServices;
using System.Windows;

namespace LandmarkLocal;

public static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(int dwProcessId);

    const int AttachParentProcess = -1;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            // WinExe does not own a console; attach to the parent terminal (or allocate one) so CLI output is visible
            // and shells wait for the process to finish.
            if (!AttachConsole(AttachParentProcess))
                AllocConsole();
            return Cli.RunAsync(args).GetAwaiter().GetResult();
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
