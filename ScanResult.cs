namespace PortScanner;

public sealed class ScanResult
{
    public int Port { get; init; }
    public string Protocol { get; init; } = "TCP";
    public string State { get; init; } = "Open";
    public string Service { get; init; } = "Unknown";
    public string Banner { get; init; } = "";
    public double LatencyMs { get; init; }
}

public sealed class ScanReport
{
    public string Target { get; init; } = "";
    public string ScanStartUtc { get; init; } = "";
    public string ScanEndUtc { get; init; } = "";
    public double ElapsedSeconds { get; init; }
    public int PortsScanned { get; init; }
    public int OpenPorts { get; init; }
    public List<ScanResult> Results { get; init; } = new();
}
