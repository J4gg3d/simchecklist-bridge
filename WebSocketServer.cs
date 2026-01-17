using Fleck;
using MSFSBridge.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Net.Http;

namespace MSFSBridge;

/// <summary>
/// WebSocket server that sends sim data to connected clients
/// </summary>
public class BridgeWebSocketServer : IDisposable
{
    private WebSocketServer? _server;
    private readonly List<IWebSocketConnection> _clients = new();
    private readonly object _lock = new();
    private bool _disposed = false;

    // Stored route for synchronization between clients
    private RouteData? _currentRoute = null;

    // Airport coordinates cache and HTTP client for API
    private readonly Dictionary<string, AirportCoords> _airportCache = new();
    private readonly HttpClient _httpClient = new();

    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    public event Action<string>? OnLog;
    public event Action<IWebSocketConnection>? OnClientConnected;
    public event Action<string?, string?>? OnUserAuthenticated;  // userId, accessToken
    public event Action<string?, string?>? OnRouteReceived;

    /// <summary>
    /// Current route (Origin, Destination) - for FlightTracker
    /// </summary>
    public (string? Origin, string? Destination) CurrentRoute =>
        (_currentRoute?.Origin, _currentRoute?.Destination);

    public int ClientCount
    {
        get
        {
            lock (_lock)
            {
                return _clients.Count;
            }
        }
    }

    /// <summary>
    /// Starts the WebSocket server
    /// </summary>
    public void Start(int port = 8080)
    {
        // Try binding to all interfaces first, fall back to localhost if permission denied
        if (!TryStartServer($"ws://0.0.0.0:{port}", port, false))
        {
            OnLog?.Invoke("Admin rights missing for network access, trying localhost...");
            if (!TryStartServer($"ws://127.0.0.1:{port}", port, true))
            {
                throw new Exception($"Could not start WebSocket server on port {port}");
            }
        }
    }

    private bool TryStartServer(string url, int port, bool isLocalOnly)
    {
        try
        {
            _server = new WebSocketServer(url);

            _server.Start(socket =>
            {
                socket.OnOpen = () =>
                {
                    lock (_lock)
                    {
                        _clients.Add(socket);
                    }
                    OnLog?.Invoke($"Client connected: {socket.ConnectionInfo.ClientIpAddress} (Total: {ClientCount})");
                    OnClientConnected?.Invoke(socket);

                    // Send current route to new client
                    if (_currentRoute != null)
                    {
                        try
                        {
                            var routeMessage = JsonConvert.SerializeObject(new { type = "route", route = _currentRoute }, _jsonSettings);
                            socket.Send(routeMessage);
                            OnLog?.Invoke($"Route sent to new client: {_currentRoute.Origin} -> {_currentRoute.Destination}");
                        }
                        catch { }
                    }
                };

                socket.OnClose = () =>
                {
                    lock (_lock)
                    {
                        _clients.Remove(socket);
                    }
                    OnLog?.Invoke($"Client disconnected: {socket.ConnectionInfo.ClientIpAddress} (Total: {ClientCount})");
                };

                socket.OnError = (ex) =>
                {
                    OnLog?.Invoke($"WebSocket error: {ex.Message}");
                    lock (_lock)
                    {
                        _clients.Remove(socket);
                    }
                };

                socket.OnMessage = (message) =>
                {
                    // Don't log auth messages fully (contains token)
                    if (message.Contains("\"type\":\"auth\""))
                    {
                        OnLog?.Invoke("Message received: [auth - token hidden]");
                    }
                    else
                    {
                        OnLog?.Invoke($"Message received: {message}");
                    }
                    HandleClientMessage(socket, message);
                };
            });

            if (isLocalOnly)
            {
                OnLog?.Invoke($"WebSocket server started on port {port} (localhost only)");
                OnLog?.Invoke("Run as administrator for network access.");
            }
            else
            {
                OnLog?.Invoke($"WebSocket server started on port {port}");
            }
            return true;
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Server start error: {ex.Message}");
            _server?.Dispose();
            _server = null;
            return false;
        }
    }

    /// <summary>
    /// Stops the WebSocket server
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            foreach (var client in _clients.ToList())
            {
                try
                {
                    client.Close();
                }
                catch { }
            }
            _clients.Clear();
        }

        _server?.Dispose();
        _server = null;

        OnLog?.Invoke("WebSocket server stopped");
    }

    /// <summary>
    /// Sends sim data to all connected clients
    /// </summary>
    public void BroadcastSimData(SimData data)
    {
        var json = JsonConvert.SerializeObject(data, _jsonSettings);
        Broadcast(json);
    }

    /// <summary>
    /// Sends landing info to all connected clients
    /// </summary>
    public void BroadcastLanding(LandingInfo landing)
    {
        var message = new
        {
            type = "landing",
            landing = landing
        };
        var json = JsonConvert.SerializeObject(message, _jsonSettings);
        Broadcast(json);
    }

    /// <summary>
    /// Sends sim data to a single client
    /// </summary>
    public void SendToClient(IWebSocketConnection client, SimData data)
    {
        try
        {
            if (client.IsAvailable)
            {
                var json = JsonConvert.SerializeObject(data, _jsonSettings);
                client.Send(json);
            }
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Send error: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a message to all connected clients
    /// </summary>
    private void Broadcast(string message)
    {
        List<IWebSocketConnection> clientsCopy;

        lock (_lock)
        {
            clientsCopy = _clients.ToList();
        }

        foreach (var client in clientsCopy)
        {
            try
            {
                if (client.IsAvailable)
                {
                    client.Send(message);
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Broadcast error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Processes messages from the client
    /// </summary>
    private void HandleClientMessage(IWebSocketConnection socket, string message)
    {
        try
        {
            var command = JsonConvert.DeserializeObject<ClientCommand>(message);
            if (command != null)
            {
                switch (command.Type)
                {
                    case "ping":
                        socket.Send(JsonConvert.SerializeObject(new { type = "pong" }));
                        break;

                    case "route":
                        HandleRouteMessage(socket, command);
                        break;

                    case "getAirport":
                        _ = HandleAirportRequest(socket, command);
                        break;

                    case "auth":
                        HandleAuthMessage(command);
                        break;
                }
            }
        }
        catch
        {
            // Ignore invalid messages
        }
    }

    /// <summary>
    /// Processes auth messages from frontend (user ID + token for flight logging)
    /// </summary>
    private void HandleAuthMessage(ClientCommand command)
    {
        try
        {
            var userId = command.Data?.ToString();
            var token = command.Token;
            OnLog?.Invoke($"Auth received: User ID = {(string.IsNullOrEmpty(userId) ? "(logged out)" : userId)}");
            OnUserAuthenticated?.Invoke(
                string.IsNullOrEmpty(userId) ? null : userId,
                string.IsNullOrEmpty(token) ? null : token
            );
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Auth error: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes route messages and synchronizes between clients
    /// </summary>
    private void HandleRouteMessage(IWebSocketConnection sender, ClientCommand command)
    {
        try
        {
            // Extract route data from command
            var routeJson = JsonConvert.SerializeObject(command.Data);
            var route = JsonConvert.DeserializeObject<RouteData>(routeJson);

            if (route != null)
            {
                _currentRoute = route;
                OnLog?.Invoke($"Route received: {route.Origin} -> {route.Destination}");

                // Trigger event for FlightTracker
                OnRouteReceived?.Invoke(route.Origin, route.Destination);

                // Broadcast route to all clients (including sender for confirmation)
                var routeMessage = JsonConvert.SerializeObject(new { type = "route", route = route }, _jsonSettings);
                Broadcast(routeMessage);
            }
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Route error: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes airport requests and fetches coordinates from API
    /// </summary>
    private async Task HandleAirportRequest(IWebSocketConnection socket, ClientCommand command)
    {
        try
        {
            var icao = command.Data?.ToString()?.Trim().ToUpper();
            if (string.IsNullOrEmpty(icao) || icao.Length < 3 || icao.Length > 4)
            {
                return;
            }

            // Get from cache if available
            if (_airportCache.TryGetValue(icao, out var cached))
            {
                var cacheResponse = JsonConvert.SerializeObject(new
                {
                    type = "airportCoords",
                    icao = icao,
                    coords = cached
                }, _jsonSettings);
                await socket.Send(cacheResponse);
                return;
            }

            // Load from API
            OnLog?.Invoke($"Loading airport from API: {icao}");
            var url = $"https://airport-data.com/api/ap_info.json?icao={icao}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                OnLog?.Invoke($"Airport API error for {icao}: {response.StatusCode}");
                await socket.Send(JsonConvert.SerializeObject(new
                {
                    type = "airportCoords",
                    icao = icao,
                    coords = (object?)null,
                    error = "not_found"
                }, _jsonSettings));
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<dynamic>(json);

#pragma warning disable CS8602 // Dereference of a possibly null reference - checked via dynamic
            if (data != null && data.latitude != null && data.longitude != null)
            {
                var coords = new AirportCoords
                {
                    Lat = (double)data.latitude,
                    Lon = (double)data.longitude
                };
#pragma warning restore CS8602

                // Save in cache
                _airportCache[icao] = coords;
                OnLog?.Invoke($"Airport loaded: {icao} ({coords.Lat:F4}, {coords.Lon:F4})");

                // Send to client
                var successResponse = JsonConvert.SerializeObject(new
                {
                    type = "airportCoords",
                    icao = icao,
                    coords = coords
                }, _jsonSettings);
                await socket.Send(successResponse);
            }
            else
            {
                OnLog?.Invoke($"Airport not found: {icao}");
                await socket.Send(JsonConvert.SerializeObject(new
                {
                    type = "airportCoords",
                    icao = icao,
                    coords = (object?)null,
                    error = "not_found"
                }, _jsonSettings));
            }
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Airport API error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Data model for client commands
/// </summary>
public class ClientCommand
{
    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("data")]
    public object? Data { get; set; }

    [JsonProperty("token")]
    public string? Token { get; set; }
}

/// <summary>
/// Data model for flight routes
/// </summary>
public class RouteData
{
    [JsonProperty("origin")]
    public string? Origin { get; set; }

    [JsonProperty("destination")]
    public string? Destination { get; set; }
}

/// <summary>
/// Data model for airport coordinates
/// </summary>
public class AirportCoords
{
    public double Lat { get; set; }
    public double Lon { get; set; }
}
