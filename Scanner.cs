using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace PortScanner;

public sealed class Scanner
{
    private static readonly Dictionary<int, string> WellKnownServices = SafeLoadPortsFile();

    // Ports that need an HTTP probe to return a banner
    private static readonly HashSet<int> HttpProbePorts = [80, 443, 8080, 8443, 8888];

    private readonly string _target;
    private readonly int _startPort;
    private readonly int _endPort;
    private readonly int _connectTimeoutMs;
    private readonly int _bannerTimeoutMs;
    private readonly int _concurrency;

    public Scanner(string target, int startPort, int endPort,
                   int connectTimeoutMs, int bannerTimeoutMs, int concurrency)
    {
        _target = target;
        _startPort = startPort;
        _endPort = endPort;
        _connectTimeoutMs = connectTimeoutMs;
        _bannerTimeoutMs = bannerTimeoutMs;
        _concurrency = concurrency;
    }

    public async Task<List<ScanResult>> RunAsync(Action<int, int>? onProgress = null)
    {
        var results = new List<ScanResult>();
        var resultsLock = new object();

        int scanned = 0;
        int total = _endPort - _startPort + 1;

        using var semaphore = new SemaphoreSlim(_concurrency);
        var tasks = new List<Task>();

        for (int port = _startPort; port <= _endPort; port++)
        {
            await semaphore.WaitAsync();
            int currentPort = port;

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var result = await ProbePortAsync(currentPort);
                    if (result is not null)
                    {
                        lock (resultsLock)
                            results.Add(result);
                    }
                }
                finally
                {
                    semaphore.Release();
                    int done = Interlocked.Increment(ref scanned);
                    if (done % 200 == 0 || done == total)
                        onProgress?.Invoke(done, total);
                }
            }));
        }

        await Task.WhenAll(tasks);

        results.Sort((a, b) => a.Port.CompareTo(b.Port));
        return results;
    }

    private async Task<ScanResult?> ProbePortAsync(int port)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(_connectTimeoutMs);

            await client.ConnectAsync(_target, port, cts.Token);
            sw.Stop();
            double latency = sw.Elapsed.TotalMilliseconds;

            string banner = await GrabBannerAsync(client, port);
            string service = IdentifyService(port, banner);

            return new ScanResult
            {
                Port = port,
                Service = service,
                Banner = CleanBanner(banner),
                LatencyMs = Math.Round(latency, 2)
            };
        }
        catch
        {
            return null; // closed / filtered
        }
    }

    private async Task<string> GrabBannerAsync(TcpClient client, int port)
    {
        try
        {
            var stream = client.GetStream();
            stream.ReadTimeout = _bannerTimeoutMs;
            stream.WriteTimeout = _bannerTimeoutMs;

            // Some services send a banner immediately on connect (SSH, FTP, SMTP).
            // Others require a probe (HTTP). Try passive read first, then probe.
            byte[] buffer = new byte[4096];

            // Passive read — wait briefly for the server to speak first
            using var readCts = new CancellationTokenSource(_bannerTimeoutMs);
            try
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), readCts.Token);
                if (bytesRead > 0)
                    return Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
            catch { /* server didn't speak first — that's fine */ }

            // Active probe — send an HTTP request for known web ports
            if (HttpProbePorts.Contains(port))
            {
                byte[] probe = Encoding.UTF8.GetBytes(
                    $"HEAD / HTTP/1.0\r\nHost: {_target}\r\nConnection: close\r\n\r\n");

                using var writeCts = new CancellationTokenSource(_bannerTimeoutMs);
                await stream.WriteAsync(probe.AsMemory(), writeCts.Token);
                await stream.FlushAsync(writeCts.Token);

                using var probeCts = new CancellationTokenSource(_bannerTimeoutMs);
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), probeCts.Token);
                if (bytesRead > 0)
                    return Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }

            return "";
        }
        catch
        {
            return "";
        }
    }

    private static string IdentifyService(int port, string banner)
    {
        // Try banner-based identification first
        string bl = banner.ToLowerInvariant();

        if (bl.Contains("ssh-"))               return "SSH";
        if (bl.Contains("220") && (bl.Contains("ftp") || bl.Contains("filezilla") || bl.Contains("vsftpd") || bl.Contains("proftpd")))
            return "FTP";
        if (bl.Contains("220") && (bl.Contains("smtp") || bl.Contains("esmtp") || bl.Contains("postfix") || bl.Contains("sendmail")))
            return "SMTP";
        if (bl.Contains("+ok") && bl.Contains("pop"))
            return "POP3";
        if (bl.Contains("* ok") && bl.Contains("imap"))
            return "IMAP";
        if (bl.StartsWith("http/") || bl.Contains("server:") || bl.Contains("<html"))
            return "HTTP";
        if (bl.Contains("mysql"))              return "MySQL";
        if (bl.Contains("postgresql"))         return "PostgreSQL";
        if (bl.Contains("redis"))              return "Redis";
        if (bl.Contains("mongo"))              return "MongoDB";
        if (bl.Contains("microsoft sql"))      return "MSSQL";
        if (bl.Contains("vnc"))                return "VNC";
        if (bl.Contains("openssl") || bl.Contains("tls"))
            return "TLS/SSL";

        // Fall back to well-known port mapping
        if (WellKnownServices.TryGetValue(port, out var svc))
            return svc;

        return "Unknown";
    }

    private static string CleanBanner(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        // Trim to first meaningful line(s), cap length, strip control chars
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            if (c == '\r' || c == '\n')
                sb.Append(' ');
            else if (!char.IsControl(c))
                sb.Append(c);
        }

        string cleaned = sb.ToString().Trim();
        return cleaned.Length > 256 ? cleaned[..256] + "..." : cleaned;
    }

    private static Dictionary<int, string> SafeLoadPortsFile()
    {
        try
        {
            return LoadPortsFile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Dictionary<int, string>();
        }
    }

    private static Dictionary<int, string> LoadPortsFile()
    {
        var dict = new Dictionary<int, string>();

        string path = Path.Combine(AppContext.BaseDirectory, "ports.csv");
        if (!File.Exists(path))
            return dict;

        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            int comma = line.IndexOf(',');
            if (comma < 1)
                continue;

            if (int.TryParse(line.AsSpan(0, comma), out int port) && port is >= 1 and <= 65535)
                dict[port] = line[(comma + 1)..];
        }

        return dict;
    }
}
