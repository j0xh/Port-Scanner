# TCP Port Scanner

A lightweight C# CLI tool for high-performance async TCP port scanning with banner grabbing, service detection, and JSON reporting.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-0078D6.svg)

## Key Features

*   **Async Scanning:** Fully asynchronous with configurable concurrency (semaphore-throttled).
*   **Banner Grabbing:** Two-phase detection with passive reading (waits for the server to speak first, e.g. SSH, FTP, SMTP) followed by an active HTTP `HEAD` probe for web ports.
*   **Service Detection:**
    *   **Banner Matching:** Identifies services from response content (SSH, FTP, SMTP, MySQL, Redis, etc.).
    *   **Port Mapping:** Falls back to a user-customizable `ports.csv` file containing 700+ known port-to-service mappings.
*   **Editable Port Database:** All port/service mappings live in a plain-text `ports.csv` file. Add, remove, or rename entries without recompiling.
*   **Dual Output:** Color-coded terminal table + formatted timestamped `.json` report saved to the working directory.
*   **Interactive Mode:** Double-click the `.exe` — prompted for target, port range, timeouts, and concurrency.
*   **CLI Mode:** Pass arguments for scripting and automation.
*   **Single-File Publish:** Ships as one portable `.exe`.

## Prerequisites

*   **Runtime:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (build) or .NET 8 Runtime (run).
*   **Permissions:** Administrator/root privileges are **not** required, but may improve results on some hosts where firewall rules restrict unprivileged sockets.

## Installation & Building

No installer required, just a single executable.

### Requirements
*   .NET 8 SDK.
*   Any OS supported by .NET 8 (Windows, Linux, macOS).

### Build

```bash
dotnet build -c Release
```

### Publish (single file)

```bash
dotnet publish -c Release -r win-x64
```

The resulting `PortScanner.exe` can be distributed standalone. `ports.csv` is copied alongside it automatically.

## User Guide

### 1. Starting a Scan — CLI

Pass arguments directly for scripting or automation:

```
PortScanner 192.168.x.x
PortScanner scanme.x.com -s 1 -e 1024
PortScanner 10.0.0.x -t 1000 -b 2000 -c 200
```

### 2. Starting a Scan — Interactive (double-click)

Just double-click `PortScanner.exe`. You will be prompted for:

1.  Target (IP or hostname)
2.  Port range
3.  Connect timeout
4.  Banner timeout
5.  Concurrency

The window stays open until you press a key.

### 3. CLI Options

| Flag | Default | Description |
|---|---|---|
| `<target>` | — | IP address or hostname (required) |
| `-s, --start` | `1` | First port in range |
| `-e, --end` | `65535` | Last port in range |
| `-t, --timeout` | `500` | TCP connect timeout (ms) |
| `-b, --banner-timeout` | `1500` | Banner read/write timeout (ms) |
| `-c, --concurrency` | `500` | Maximum simultaneous connections |
| `-h, --help` | — | Show usage |

### 4. Reading the Output

The terminal displays a color-coded table of open ports:

```
  PORT      STATE     SERVICE             LATENCY     BANNER
  ------------------------------------------------------------------------------------------
  22        Open      SSH                 1.23 ms     SSH-2.0-OpenSSH_8.9p1 Ubuntu-3ubuntu0.6
  80        Open      HTTP                3.45 ms     HTTP/1.1 200 OK Server: nginx/1.24.0
  443       Open      HTTPS               2.10 ms
  3306      Open      MySQL               4.67 ms     5.7.42-0ubuntu0.18.04.1
```

*   **Service:** Detected from the banner first, then from `ports.csv` if no banner match.
*   **Banner:** The first line of whatever the server sends back, useful for version fingerprinting.

### 5. Editing the Port Database

Open `ports.csv` in any text editor. The format is simple:

```
# Lines starting with # are comments
port,service
22,SSH
80,HTTP
8080,HTTP-Proxy
```

Add or remove entries as needed. Changes take effect the next time the scanner runs — no recompilation required.

### 6. JSON Output

A file named `scan_<target>_<timestamp>.json` is saved in the working directory:

```json
{
  "target": "192.168.1.1",
  "scanStartUtc": "2025-01-15T12:00:00.0000000Z",
  "scanEndUtc": "2025-01-15T12:01:32.0000000Z",
  "elapsedSeconds": 92.14,
  "portsScanned": 65535,
  "openPorts": 4,
  "results": [
    {
      "port": 22,
      "protocol": "TCP",
      "state": "Open",
      "service": "SSH",
      "banner": "SSH-2.0-OpenSSH_8.9p1 Ubuntu-3ubuntu0.6",
      "latencyMs": 1.23
    }
  ]
}
```

## Disclaimer

This tool is for educational and administrative network auditing purposes. It performs standard TCP connect scans using .NET's `TcpClient`. It does not perform SYN stealth scans, packet sniffing, SSL interception, or traffic modification.

## License

MIT
