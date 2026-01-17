# MSFSBridge

**[Deutsch](README.de.md)** | English

Bridge server that connects Microsoft Flight Simulator 2024 with [SimChecklist](https://simchecklist.app).

## What does the Bridge do?

The Bridge runs **locally on your PC** and:

1. **Reads flight data from MSFS 2024** via SimConnect (the official Microsoft API)
2. **Sends this data via WebSocket** to the SimChecklist website

### Transmitted Data

| Category | Data |
|----------|------|
| **Position** | Latitude, Longitude, Altitude, Ground Altitude |
| **Speed** | Ground Speed, Vertical Speed, Airspeed |
| **Attitude** | Pitch, Bank, Heading, Angle of Attack |
| **G-Forces** | Vertical G, Lateral G, Longitudinal G |
| **Status** | On Ground, Gear Position, Flaps, Engines Running |
| **Systems** | Lights, Electrical, APU, Anti-Ice, Transponder |
| **ATC** | Callsign, Airline, Flight Number |
| **GPS/Flight Plan** | Waypoints, Destination, ETE |

### What is NOT transmitted?

- No personal data from your PC
- No files outside of MSFS
- No screenshots or screen contents
- No keyboard inputs

## Architecture

```
┌─────────────────┐     SimConnect     ┌─────────────────┐
│  MSFS 2024      │◄──────────────────►│  Bridge         │
│  (Simulator)    │                    │  (This App)     │
└─────────────────┘                    └────────┬────────┘
                                                │
                                                │ WebSocket (Port 8080)
                                                │
                                                ▼
                                       ┌─────────────────┐
                                       │  SimChecklist   │
                                       │  (Browser)      │
                                       └─────────────────┘
```

## Features

- **Auto-Connect**: Automatically connects to MSFS when running
- **Auto-Retry**: Retries every 5 seconds if MSFS is not running
- **Auto-Update**: Checks for new versions on startup and updates with one click
- **Landing Detection**: Automatically detects and rates landings (1-5 stars)
- **Glidepath Recording**: Records the last 60 seconds of approach
- **Flight Logging**: Saves flights automatically when logged in to SimFlyCorp
- **Demo Mode**: Simulated data when MSFS is not running (for testing)

## Download

Download the latest version from [Releases](https://github.com/J4gg3d/simchecklist-bridge/releases).

## Quick Start

1. Download and extract `MSFSBridge-vX.X.X.zip`
2. Run `MSFSBridge.exe`
3. Open https://simchecklist.app in your browser
4. The website automatically connects to the Bridge

## Configuration

Create a `.env` file in the same folder as `MSFSBridge.exe` for optional configuration:

```env
# Custom ports (if default ports are blocked)
WEBSOCKET_PORT=8090
HTTP_PORT=8091
```

### Port Configuration

The default ports are:
- **WebSocket**: 8080
- **HTTP** (for tablets): 8081

If port 8080 is blocked (common with Hyper-V or Docker), set a custom port in `.env` and configure the same port in the website settings.

## Tablet Access

### Option A: Via simchecklist.app (Recommended)
1. Open https://simchecklist.app on your PC
2. Run the Bridge
3. Website connects automatically

### Option B: Local Network (for tablets)
1. Run the Bridge on your PC
2. Note the URL shown in the console (e.g., `http://192.168.1.100:8081`)
3. Open this URL on your tablet
4. PC and tablet must be on the same WiFi network

**Note**: The HTTP server for tablets requires a `www` folder with website files. Without it, use https://simchecklist.app directly.

## Commands

While the Bridge is running:

| Key | Action |
|-----|--------|
| `C` | Connect to MSFS (manual) |
| `D` | Disconnect from MSFS |
| `R` | Enable auto-retry |
| `S` | Show status |
| `Q` | Quit |

## Landing Rating System

| Rating | Vertical Speed | Stars |
|--------|----------------|-------|
| Perfect | < 100 ft/min | ★★★★★ |
| Good | 100-200 ft/min | ★★★★☆ |
| Acceptable | 200-300 ft/min | ★★★☆☆ |
| Hard | 300-500 ft/min | ★★☆☆☆ |
| Very Hard | > 500 ft/min | ★☆☆☆☆ |

## Firewall

If the Bridge can't connect, you may need to allow it through Windows Firewall:

1. Open Windows Firewall settings
2. Click "Allow an app through firewall"
3. Add `MSFSBridge.exe`
4. Enable for Private networks

## Requirements

- Windows 10/11
- Microsoft Flight Simulator 2024
- .NET 8.0 Runtime (included in Windows 11)

## Building from Source

```bash
# Clone repository
git clone https://github.com/J4gg3d/simchecklist-bridge.git
cd simchecklist-bridge

# Build
dotnet build

# Run
dotnet run
```

SimConnect DLLs are included in the `libs/` folder.

## Troubleshooting

### Bridge doesn't connect to MSFS
1. Make sure MSFS 2024 is running (not just the launcher)
2. Restart the Bridge
3. Check if .NET 8 Runtime is installed

### "Port already in use"
Another application is using port 8080. Either:
- Close the other application
- Set a custom port in `.env`

### Website shows "Not connected"
1. Make sure the Bridge is running
2. Check if port 8080 is allowed in the firewall
3. If using a custom port, make sure it matches in website settings

## Privacy

- All data stays in your local network
- The Bridge doesn't send data to external servers (except optionally to SimFlyCorp if enabled)
- No tracking cookies, no analytics
- Source code is fully visible

## License

MIT License - see [LICENSE](LICENSE)

## Links

- [SimChecklist.app](https://simchecklist.app) - The main application
- [SimFlyCorp](https://simchecklist.app/simflycorp/info) - Pilot career system
- [Discord](https://discord.gg/4YzfNCcU) - Community & Support
