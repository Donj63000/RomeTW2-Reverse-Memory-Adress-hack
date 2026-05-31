using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using Rome2Explorer.Memory;
using Rome2Explorer.Trace;

namespace Rome2Explorer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!IsRunningAsAdministrator())
        {
            RelaunchAsAdministrator(e.Args);
            Shutdown();
            return;
        }

        if (e.Args.Any(arg => string.Equals(arg, "--export-memory-map", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(ExportMemoryMapFromCommandLine());
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchAsAdministrator(IReadOnlyList<string> args)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            MessageBox.Show(
                "DonjHACK doit etre lance en administrateur pour lire Rome2.exe. Relance l'application avec clic droit > Executer en tant qu'administrateur.",
                "DonjHACK - droits administrateur requis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Je force une relance elevee ici pour eviter l'erreur Windows 5 sur OpenProcess.
            // Je relance en administrateur pour que l'attache, les scans et l'ecriture controlee aient les memes droits que Rome2.exe.
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = string.Join(" ", args.Select(QuoteArgument)),
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch (Win32Exception)
        {
            MessageBox.Show(
                "Elevation annulee. DonjHACK ne peut pas s'attacher a Rome2.exe sans les memes droits que le jeu.",
                "DonjHACK - droits administrateur requis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        return argument.Any(char.IsWhiteSpace)
            ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
    }

    private static int ExportMemoryMapFromCommandLine()
    {
        try
        {
            using var reader = new ProcessMemoryReader();
            reader.AttachToRome2();
            var snapshot = reader.CaptureMemoryMap();
            new MemoryMapExporter().Export(snapshot);
            return 0;
        }
        catch (Exception ex)
        {
            WriteCommandLineError(ex);
            return 1;
        }
    }

    private static void WriteCommandLineError(Exception exception)
    {
        try
        {
            Console.Error.WriteLine(exception);

            var root = DonjHackPathResolver.ResolveRoot();
            var logDirectory = Path.Combine(root, "logs");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, $"command-line-export-error-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(logPath, exception.ToString());
        }
        catch
        {
            // En mode CLI de verification, on evite qu'une erreur de log masque l'erreur originale.
        }
    }
}
