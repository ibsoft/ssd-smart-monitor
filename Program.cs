using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SsdHealthInspector;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0) return RunConsole(args);

        // Console subsystem ensures piping and exit codes work reliably for --json.
        // Remove the console for the normal Windows Forms experience.
        FreeConsole();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }

    private static int RunConsole(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string? output = null;
        bool json = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLowerInvariant();
            if (arg is "--help" or "-h" or "/?")
            {
                Console.WriteLine("SSD Health Inspector 1.0 (Windows 10/11 x64)");
                Console.WriteLine("Usage: SsdHealthInspector.exe [--json] [--output PATH]");
                Console.WriteLine("       SsdHealthInspector.exe --help");
                Console.WriteLine("--json         Print a complete JSON health report to stdout.");
                Console.WriteLine("--output PATH  Save JSON to a file. Can be used with --json.");
                Console.WriteLine("Exit codes: 0=healthy, 1=warning/unknown, 2=replace soon, 3=replace now, 4=error.");
                return 0;
            }
            if (arg == "--json") json = true;
            else if (arg == "--output" && i + 1 < args.Length) output = args[++i];
            else
            {
                Console.Error.WriteLine("Unknown/incomplete argument: " + args[i]);
                return 4;
            }
        }
        if (!json && output is null)
        {
            Console.Error.WriteLine("Specify --json or --output PATH. Run --help for usage.");
            return 4;
        }
        try
        {
            var disks = DiskDiscovery.ScanAsync().GetAwaiter().GetResult();
            if (disks.Count == 0)
            {
                Console.Error.WriteLine("No physical disks could be enumerated. Try Run as administrator.");
                return 4;
            }
            string result = ReportWriter.CreateJson(disks);
            if (output is not null)
            {
                string path = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, result + Environment.NewLine, new UTF8Encoding(false));
            }
            if (json) Console.WriteLine(result);
            return disks.Max(d => HealthEvaluator.Evaluate(d).Level switch
            {
                DecisionLevel.ReplaceNow => 3,
                DecisionLevel.ReplaceSoon => 2,
                DecisionLevel.Monitor or DecisionLevel.Unknown => 1,
                _ => 0
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SSD health scan failed: " + ex.Message);
            return 4;
        }
    }
}
