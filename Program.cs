using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PortScanner;

internal static partial class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static async Task<int> Main(string[] args)
    {
        bool interactive = args.Length == 0 && !Console.IsInputRedirected;

        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintBanner();
            PrintUsage();
            WaitForExit(interactive);
            return 0;
        }

        string target;
        int startPort = 1;
        int endPort = 65535;
        int timeout = 500;
        int bannerTimeout = 1500;
        int concurrency = 500;

        if (interactive)
        {
            PrintBanner();
            target = PromptTarget();
            (startPort, endPort) = PromptPortRange();
            timeout = PromptInt("  Connect timeout ms", 500, 50, 10000);
            bannerTimeout = PromptInt("  Banner timeout ms ", 1500, 100, 10000);
            concurrency = PromptInt("  Concurrency       ", 500, 1, 5000);
            Console.WriteLine();
        }
        else if (args.Length == 0)
        {
            // No args and stdin is redirected — can't prompt, show usage
            PrintBanner();
            PrintUsage();
            return 1;
        }
        else
        {
            target = args[0];

            if (!IsValidTarget(target))
            {
                PrintBanner();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Error: '{target}' is not a valid IP address or hostname.");
                Console.ResetColor();
                WaitForExit(false);
                return 1;
            }

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-s" or "--start" when i + 1 < args.Length:
                        if (!int.TryParse(args[++i], out startPort))
                            return ArgError($"Invalid value for --start: '{args[i]}'");
                        break;
                    case "-e" or "--end" when i + 1 < args.Length:
                        if (!int.TryParse(args[++i], out endPort))
                            return ArgError($"Invalid value for --end: '{args[i]}'");
                        break;
                    case "-t" or "--timeout" when i + 1 < args.Length:
                        if (!int.TryParse(args[++i], out timeout))
                            return ArgError($"Invalid value for --timeout: '{args[i]}'");
                        break;
                    case "-b" or "--banner-timeout" when i + 1 < args.Length:
                        if (!int.TryParse(args[++i], out bannerTimeout))
                            return ArgError($"Invalid value for --banner-timeout: '{args[i]}'");
                        break;
                    case "-c" or "--concurrency" when i + 1 < args.Length:
                        if (!int.TryParse(args[++i], out concurrency))
                            return ArgError($"Invalid value for --concurrency: '{args[i]}'");
                        break;
                    default:
                        return ArgError($"Unknown option: '{args[i]}'");
                }
            }

            PrintBanner();
        }

        startPort = Math.Clamp(startPort, 1, 65535);
        endPort = Math.Clamp(endPort, startPort, 65535);
        timeout = Math.Clamp(timeout, 50, 30000);
        bannerTimeout = Math.Clamp(bannerTimeout, 100, 30000);
        concurrency = Math.Clamp(concurrency, 1, 5000);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  -- Configuration ------------------------------------");
        Console.ResetColor();
        Console.WriteLine($"  Target       : {target}");
        Console.WriteLine($"  Port Range   : {startPort} - {endPort}");
        Console.WriteLine($"  Timeout      : {timeout} ms (connect) / {bannerTimeout} ms (banner)");
        Console.WriteLine($"  Concurrency  : {concurrency}");
        Console.WriteLine();

        try
        {
            var scanner = new Scanner(target, startPort, endPort, timeout, bannerTimeout, concurrency);
            var totalSw = Stopwatch.StartNew();
            var startUtc = DateTime.UtcNow;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  Scanning ...");

            var results = await scanner.RunAsync((done, total) =>
            {
                double pct = (double)done / total * 100;
                Console.Write($"\r  Scanning [{done}/{total}] {pct,6:F1}%   ");
            });

            totalSw.Stop();
            var endUtc = DateTime.UtcNow;

            Console.WriteLine();
            Console.ResetColor();
            Console.WriteLine();

            // ── Terminal output 
            if (results.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  No open ports found.");
                Console.ResetColor();
            }
            else
            {
                string header = $"  {"PORT",-10}{"STATE",-10}{"SERVICE",-20}{"LATENCY",-12}{"BANNER"}";
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(header);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  " + new string('-', 90));
                Console.ResetColor();

                foreach (var r in results)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"  {r.Port,-10}");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"{"Open",-10}");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"{r.Service,-20}");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"{r.LatencyMs + " ms",-12}");
                    Console.ForegroundColor = ConsoleColor.DarkYellow;

                    string bannerDisplay = r.Banner.Length > 60 ? r.Banner[..60] + "..." : r.Banner;
                    Console.WriteLine(bannerDisplay);

                    Console.ResetColor();
                }
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  -- Summary ------------------------------------------");
            Console.ResetColor();
            Console.WriteLine($"  Completed in {totalSw.Elapsed.TotalSeconds:F2}s - " +
                              $"{endPort - startPort + 1} ports scanned, {results.Count} open");

            // ── JSON output 
            var report = new ScanReport
            {
                Target = target,
                ScanStartUtc = startUtc.ToString("o"),
                ScanEndUtc = endUtc.ToString("o"),
                ElapsedSeconds = Math.Round(totalSw.Elapsed.TotalSeconds, 2),
                PortsScanned = endPort - startPort + 1,
                OpenPorts = results.Count,
                Results = results
            };

            string json = JsonSerializer.Serialize(report, JsonOpts);
            string safeTarget = SafeFilename().Replace(target, "_");
            string filename = $"scan_{safeTarget}_{startUtc:yyyyMMdd_HHmmss}.json";

            try
            {
                await File.WriteAllTextAsync(filename, json);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  Report saved -> {Path.GetFullPath(filename)}");
                Console.ResetColor();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Warning: Could not save report — {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine();
            WaitForExit(interactive);
            return 0;
        }
        catch (Exception ex)
        {
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine($"  Error: {ex.Message}");
            Console.ResetColor();
            WaitForExit(interactive);
            return 1;
        }
    }

    // ── Interactive prompts

    private static string PromptTarget()
    {
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("  Target (IP or hostname): ");
            Console.ResetColor();
            string? input = Console.ReadLine()?.Trim();

            if (!string.IsNullOrEmpty(input) && IsValidTarget(input))
                return input;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Invalid target. Enter a valid IP address or hostname.");
            Console.ResetColor();
        }
    }

    private static (int start, int end) PromptPortRange()
    {
        int start = PromptInt("  Start port        ", 1, 1, 65535);
        int end = PromptInt("  End port          ", 65535, start, 65535);
        return (start, end);
    }

    private static int PromptInt(string label, int defaultVal, int min, int max)
    {
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{label} [{defaultVal}]: ");
            Console.ResetColor();
            string? input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
                return defaultVal;

            if (int.TryParse(input, out int val) && val >= min && val <= max)
                return val;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Enter a number between {min} and {max}.");
            Console.ResetColor();
        }
    }

    private static bool IsValidTarget(string target)
    {
        if (IPAddress.TryParse(target, out _))
            return true;

        return Uri.CheckHostName(target) != UriHostNameType.Unknown;
    }

    // ── Display helpers 

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("""

         +======================================+
         |       j0xh's Port Scanner v1.0       |
         |    Async TCP - Banner Grab - JSON    |                                     
         +======================================+
        """);
        Console.ResetColor();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
          USAGE
            PortScanner <target> [options]
            PortScanner                        (interactive mode)

          ARGUMENTS
            target               IP address or hostname to scan

          OPTIONS
            -s, --start <port>          Start port         (default: 1)
            -e, --end   <port>          End port           (default: 65535)
            -t, --timeout <ms>          Connect timeout    (default: 500 ms)
            -b, --banner-timeout <ms>   Banner timeout     (default: 1500 ms)
            -c, --concurrency <n>       Max connections    (default: 500)
            -h, --help                  Show this help

          EXAMPLES
            PortScanner 192.168.x.x
            PortScanner scanme.x.com -s 1 -e 1024
            PortScanner 10.0.0.x -t 1000 -b 2000 -c 200

          OUTPUT
            Results are printed to the terminal and saved as a
            timestamped JSON file in the current working directory.
        """);
    }

    private static void WaitForExit(bool interactive)
    {
        if (!interactive)
            return;

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Press any key to exit ...");
        Console.ResetColor();

        try
        {
            Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
            // stdin is not available (e.g. redirected pipe) — just return
        }
    }

    private static int ArgError(string message)
    {
        PrintBanner();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  Error: {message}");
        Console.ResetColor();
        Console.WriteLine("  Run with --help for usage information.");
        return 1;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9\-_]")]
    private static partial Regex SafeFilename();
}
