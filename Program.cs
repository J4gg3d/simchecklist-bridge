using MSFSBridge;
using DotNetEnv;
using System.Net;
using System.Net.Sockets;

// Helper function: Get local IP addresses
static List<string> GetLocalIPAddresses()
{
    var ips = new List<string>();
    try
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                ips.Add(ip.ToString());
            }
        }
    }
    catch { }
    return ips;
}

// Load .env files (Bridge .env has priority for service keys)
var envPaths = new[] {
    // Bridge-specific .env (with service key)
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".env"),  // When using dotnet run
    Path.Combine(AppContext.BaseDirectory, ".env"),  // For release build
    // Root .env as fallback
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env"),
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env"),
};

// Load all found .env files (later ones override earlier ones)
foreach (var envPath in envPaths)
{
    if (File.Exists(envPath))
    {
        Env.Load(envPath);
        Console.WriteLine($"[CONFIG] .env loaded: {Path.GetFullPath(envPath)}");
    }
}

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║           MSFS Checklist - Bridge Server                     ║");
Console.WriteLine($"║           Version {MSFSBridge.UpdateChecker.CURRENT_VERSION,-10}                                   ║");
Console.WriteLine("║                                                              ║");
Console.WriteLine("║  Connects Microsoft Flight Simulator with the checklist      ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Check for updates
await MSFSBridge.UpdateChecker.CheckAndPromptForUpdateAsync();

// Load ports from .env (with fallback to default ports)
var wsPortStr = Environment.GetEnvironmentVariable("WEBSOCKET_PORT");
var httpPortStr = Environment.GetEnvironmentVariable("HTTP_PORT");

int WEBSOCKET_PORT = 8500;
int HTTP_PORT = 8501;

if (int.TryParse(wsPortStr, out var customWsPort))
{
    WEBSOCKET_PORT = customWsPort;
    Console.WriteLine($"[CONFIG] WebSocket port from .env: {WEBSOCKET_PORT}");
}
if (int.TryParse(httpPortStr, out var customHttpPort))
{
    HTTP_PORT = customHttpPort;
    Console.WriteLine($"[CONFIG] HTTP port from .env: {HTTP_PORT}");
}

// WWW folder for static files (website)
var wwwRoot = Path.Combine(AppContext.BaseDirectory, "www");

// Start static web server (for tablets)
using var staticServer = new StaticFileServer(wwwRoot);
staticServer.OnLog += (message) => Console.WriteLine($"[HTTP] {message}");
staticServer.Start(HTTP_PORT);
string? sessionCode = null;

// Create and start WebSocket server
using var webSocketServer = new BridgeWebSocketServer();
webSocketServer.OnLog += (message) => Console.WriteLine($"[WS] {message}");

// Store session code for new client connections
string? activeSessionCode = null;

// Send session code immediately when new client connects
webSocketServer.OnClientConnected += (client) =>
{
    if (!string.IsNullOrEmpty(activeSessionCode))
    {
        var welcomeData = new MSFSBridge.Models.SimData
        {
            Connected = false,
            SessionCode = activeSessionCode
        };
        webSocketServer.SendToClient(client, welcomeData);
        Console.WriteLine($"[WS] Session code sent to new client: {activeSessionCode}");
    }
};

try
{
    webSocketServer.Start(WEBSOCKET_PORT);
}
catch (Exception ex)
{
    Console.WriteLine($"[ERROR] WebSocket server could not be started: {ex.Message}");
    Console.WriteLine($"        Port {WEBSOCKET_PORT} may already be in use.");
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
    return;
}

// Create Supabase session manager (optional)
using var supabaseSession = new SupabaseSessionManager();
supabaseSession.OnLog += (message) => Console.WriteLine($"[SESSION] {message}");
supabaseSession.OnError += (error) => Console.WriteLine($"[SESSION ERROR] {error}");

// Start session if Supabase is configured
if (SupabaseSessionManager.IsConfigured())
{
    Console.WriteLine();
    Console.WriteLine("[SESSION] Supabase configured - starting remote session...");
    sessionCode = await supabaseSession.StartSessionAsync();
    activeSessionCode = sessionCode; // Store for new client connections

    if (sessionCode != null)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    REMOTE SESSION ACTIVE                     ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║                                                              ║");
        Console.WriteLine($"║     Session Code:   {sessionCode}                         ║");
        Console.WriteLine($"║                                                              ║");
        Console.WriteLine("║  Enter this code on your tablet/phone to receive             ║");
        Console.WriteLine("║  live flight data.                                           ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }
}
else
{
    // Show local network info for tablet access
    var localIPs = GetLocalIPAddresses();
    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║                  TABLET ACCESS (LOCAL NETWORK)               ║");
    Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
    Console.WriteLine("║                                                              ║");
    if (localIPs.Count > 0)
    {
        Console.WriteLine("║  Open this address in your tablet's browser:                 ║");
        Console.WriteLine("║                                                              ║");
        foreach (var ip in localIPs)
        {
            var urlDisplay = $"http://{ip}:{HTTP_PORT}".PadRight(30);
            Console.WriteLine($"║     {urlDisplay}                   ║");
        }
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║  Or on this PC:                                              ║");
        Console.WriteLine($"║     http://localhost:{HTTP_PORT}                                    ║");
    }
    else
    {
        Console.WriteLine("║  No network connection found.                                ║");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║  Local access:                                               ║");
        Console.WriteLine($"║     http://localhost:{HTTP_PORT}                                    ║");
    }
    Console.WriteLine("║                                                              ║");
    Console.WriteLine("║  Tablet and PC must be on the same WiFi network!             ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
}

// Create SimConnect manager
using var simConnect = new SimConnectManager();

// Create Supabase client for flight logging
using var supabaseClient = new SupabaseClient();
supabaseClient.OnLog += (message) => Console.WriteLine($"[FLIGHT-LOG] {message}");
supabaseClient.OnError += (error) => Console.WriteLine($"[FLIGHT-LOG ERROR] {error}");

if (supabaseClient.IsConfigured)
{
    Console.WriteLine("[FLIGHT-LOG] Supabase configured - flights will be saved");
}

// Set session code for flight logging (for anonymous flights)
if (!string.IsNullOrEmpty(activeSessionCode))
{
    simConnect.SetSessionCode(activeSessionCode);
}

// Receive user authentication from frontend
string? currentUserId = null;
webSocketServer.OnUserAuthenticated += (userId, accessToken) =>
{
    currentUserId = userId;
    simConnect.SetUserId(userId);
    supabaseClient.SetUserToken(accessToken);
    Console.WriteLine($"[AUTH] User ID set: {(string.IsNullOrEmpty(userId) ? "(none)" : userId)}");
};

// Receive route from frontend (for FlightTracker)
webSocketServer.OnRouteReceived += (origin, destination) =>
{
    simConnect.SetRoute(origin, destination);
};

simConnect.OnStatusChanged += (status) => Console.WriteLine($"[SIM] {status}");
simConnect.OnError += (error) => Console.WriteLine($"[SIM ERROR] {error}");
simConnect.OnLandingDetected += (landing) =>
{
    // Broadcast landing to all clients
    webSocketServer.BroadcastLanding(landing);
};
simConnect.OnFlightCompleted += async (flight) =>
{
    // Save flight to Supabase
    if (supabaseClient.IsConfigured)
    {
        await supabaseClient.SaveFlightAsync(flight);
    }
};
simConnect.OnDataReceived += async (data) =>
{
    // Add session code if active
    if (supabaseSession.IsConnected && supabaseSession.SessionCode != null)
    {
        data.SessionCode = supabaseSession.SessionCode;
    }

    // Send data to all connected WebSocket clients
    webSocketServer.BroadcastSimData(data);

    // Also send to remote session (if active)
    if (supabaseSession.IsConnected)
    {
        await supabaseSession.BroadcastSimDataAsync(data);
    }
};

// Admin user ID for test functions (from .env)
var adminUserId = Environment.GetEnvironmentVariable("ADMIN_USER_ID");
bool isAdminConfigured = !string.IsNullOrEmpty(adminUserId);

Console.WriteLine();
Console.WriteLine($"WebSocket Server: ws://localhost:{WEBSOCKET_PORT}");
Console.WriteLine($"HTTP Server:      http://localhost:{HTTP_PORT}");
if (isAdminConfigured)
{
    Console.WriteLine($"Admin ID:         {adminUserId?.Substring(0, 8)}... (configured)");
}
Console.WriteLine();
Console.WriteLine("Commands:");
Console.WriteLine("  [C] Connect to MSFS (manual)");
Console.WriteLine("  [D] Disconnect from MSFS");
Console.WriteLine("  [R] Enable auto-retry");
Console.WriteLine("  [S] Show status");
if (isAdminConfigured)
{
    Console.WriteLine("  [T] Create test flight (admin only)");
}
Console.WriteLine("  [Q] Quit");
Console.WriteLine();

// Auto-connect configuration
const int AUTO_RETRY_INTERVAL_MS = 5000; // Retry every 5 seconds
bool autoRetryEnabled = true;
DateTime lastRetryTime = DateTime.MinValue;

// Automatic connection attempt at startup
Console.WriteLine("[AUTO] Attempting to connect to MSFS automatically...");
if (!simConnect.Connect())
{
    Console.WriteLine($"[AUTO] MSFS not found. Auto-retry every {AUTO_RETRY_INTERVAL_MS / 1000} seconds...");
    Console.WriteLine("       Start MSFS and the connection will be established automatically.");
    Console.WriteLine();
}
else
{
    autoRetryEnabled = false; // No more retries needed when connected
}

// Main loop
bool running = true;
while (running)
{
    // Auto-retry logic: If not connected and auto-retry enabled
    if (autoRetryEnabled && !simConnect.IsConnected)
    {
        if ((DateTime.Now - lastRetryTime).TotalMilliseconds >= AUTO_RETRY_INTERVAL_MS)
        {
            lastRetryTime = DateTime.Now;
            Console.WriteLine("[AUTO] Connection attempt...");
            if (simConnect.Connect())
            {
                Console.WriteLine("[AUTO] Successfully connected to MSFS!");
                autoRetryEnabled = false;
            }
        }
    }

    // If connection is lost, re-enable auto-retry
    if (!autoRetryEnabled && !simConnect.IsConnected)
    {
        Console.WriteLine("[AUTO] Connection lost. Enabling auto-retry...");
        autoRetryEnabled = true;
        lastRetryTime = DateTime.Now;
    }

    if (Console.KeyAvailable)
    {
        var key = Console.ReadKey(true).Key;

        switch (key)
        {
            case ConsoleKey.C:
                if (!simConnect.IsConnected)
                {
                    Console.WriteLine("\nConnecting to MSFS...");
                    if (simConnect.Connect())
                    {
                        autoRetryEnabled = false;
                    }
                }
                else
                {
                    Console.WriteLine("\nAlready connected.");
                }
                break;

            case ConsoleKey.D:
                if (simConnect.IsConnected)
                {
                    Console.WriteLine("\nDisconnecting...");
                    simConnect.Disconnect();
                    autoRetryEnabled = false; // Manually disconnected, no auto-retry
                    Console.WriteLine("Auto-retry disabled. Press [C] to connect manually or [R] for auto-retry.");
                }
                else
                {
                    Console.WriteLine("\nNot connected.");
                }
                break;

            case ConsoleKey.R:
                if (!autoRetryEnabled)
                {
                    autoRetryEnabled = true;
                    lastRetryTime = DateTime.MinValue; // Try immediately
                    Console.WriteLine("\nAuto-retry enabled.");
                }
                else
                {
                    Console.WriteLine("\nAuto-retry is already active.");
                }
                break;

            case ConsoleKey.S:
                Console.WriteLine();
                Console.WriteLine("=== STATUS ===");
                Console.WriteLine($"  SimConnect: {(simConnect.IsConnected ? "Connected" : "Not connected")}");
                Console.WriteLine($"  Auto-Retry: {(autoRetryEnabled ? "Active" : "Disabled")}");
                Console.WriteLine($"  WebSocket Clients: {webSocketServer.ClientCount}");
                Console.WriteLine($"  HTTP Server: Port {HTTP_PORT}");
                Console.WriteLine($"  Remote Session: {(supabaseSession.IsConnected ? $"Active ({supabaseSession.SessionCode})" : "Not active")}");
                Console.WriteLine("==============");
                break;

            case ConsoleKey.T:
                // Admin-only test function
                if (!isAdminConfigured)
                {
                    Console.WriteLine("\n[TEST] Admin ID not configured! Set ADMIN_USER_ID in .env");
                }
                else if (string.IsNullOrEmpty(currentUserId))
                {
                    Console.WriteLine("\n[TEST] No user logged in! Please log in on the website first.");
                }
                else if (currentUserId != adminUserId)
                {
                    Console.WriteLine("\n[TEST] Access denied! Only the admin can create test flights.");
                    Console.WriteLine($"       Logged in: {currentUserId.Substring(0, 8)}...");
                    Console.WriteLine($"       Admin:     {adminUserId?.Substring(0, 8)}...");
                }
                else if (!supabaseClient.IsConfigured)
                {
                    Console.WriteLine("\n[TEST] Supabase not configured!");
                }
                else
                {
                    Console.WriteLine("\n[TEST] Admin verified - creating test flight...");
                    var random = new Random();
                    var airports = new[] { "EDDF", "KJFK", "EGLL", "LFPG", "KLAX", "KEWR", "KORD", "LEMD", "LIRF", "EHAM" };
                    var aircraft = new[] { "Airbus A330-200", "Boeing 737 MAX 8", "Pilatus PC-12 NGX" };

                    var testFlight = new MSFSBridge.Models.FlightLog
                    {
                        UserId = currentUserId,
                        Origin = airports[random.Next(airports.Length)],
                        Destination = airports[random.Next(airports.Length)],
                        AircraftType = aircraft[random.Next(aircraft.Length)],
                        DepartureTime = DateTime.UtcNow.AddMinutes(-random.Next(30, 180)),
                        ArrivalTime = DateTime.UtcNow,
                        FlightDurationSeconds = random.Next(1800, 10800), // 30 min to 3 hours
                        DistanceNm = random.Next(100, 3000),
                        MaxAltitudeFt = random.Next(10000, 41000),
                        LandingRating = random.Next(1, 6),
                        LandingVs = -random.Next(50, 500),
                        LandingGforce = 1.0 + random.NextDouble() * 0.5
                    };

                    // Calculate score
                    testFlight.CalculateScore();

                    Console.WriteLine($"[TEST] {testFlight.Origin} -> {testFlight.Destination}, {testFlight.DistanceNm} NM, Score: {testFlight.Score}");
                    _ = Task.Run(async () =>
                    {
                        var success = await supabaseClient.SaveFlightAsync(testFlight);
                        if (success)
                        {
                            Console.WriteLine("[TEST] Test flight saved successfully!");
                        }
                    });
                }
                break;

            case ConsoleKey.Q:
                Console.WriteLine("\nShutting down...");
                running = false;
                break;
        }
    }

    Thread.Sleep(50);
}

// Cleanup
simConnect.Disconnect();
webSocketServer.Stop();
await supabaseSession.StopSessionAsync();

Console.WriteLine("Bridge server stopped.");
