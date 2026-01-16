using Fleck;
using MSFSBridge.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Net.Http;

namespace MSFSBridge;

/// <summary>
/// WebSocket-Server der Sim-Daten an verbundene Clients sendet
/// </summary>
public class BridgeWebSocketServer : IDisposable
{
    private WebSocketServer? _server;
    private readonly List<IWebSocketConnection> _clients = new();
    private readonly object _lock = new();
    private bool _disposed = false;

    // Gespeicherte Route für Synchronisation zwischen Clients
    private RouteData? _currentRoute = null;

    // Airport-Koordinaten Cache und HTTP-Client für API
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
    /// Aktuelle Route (Origin, Destination) - für FlightTracker
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
    /// Startet den WebSocket-Server
    /// </summary>
    public void Start(int port = 8080)
    {
        try
        {
            _server = new WebSocketServer($"ws://0.0.0.0:{port}");

            _server.Start(socket =>
            {
                socket.OnOpen = () =>
                {
                    lock (_lock)
                    {
                        _clients.Add(socket);
                    }
                    OnLog?.Invoke($"Client verbunden: {socket.ConnectionInfo.ClientIpAddress} (Gesamt: {ClientCount})");
                    OnClientConnected?.Invoke(socket);

                    // Aktuelle Route an neuen Client senden
                    if (_currentRoute != null)
                    {
                        try
                        {
                            var routeMessage = JsonConvert.SerializeObject(new { type = "route", route = _currentRoute }, _jsonSettings);
                            socket.Send(routeMessage);
                            OnLog?.Invoke($"Route an neuen Client gesendet: {_currentRoute.Origin} → {_currentRoute.Destination}");
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
                    OnLog?.Invoke($"Client getrennt: {socket.ConnectionInfo.ClientIpAddress} (Gesamt: {ClientCount})");
                };

                socket.OnError = (ex) =>
                {
                    OnLog?.Invoke($"WebSocket-Fehler: {ex.Message}");
                    lock (_lock)
                    {
                        _clients.Remove(socket);
                    }
                };

                socket.OnMessage = (message) =>
                {
                    // Für zukünftige Befehle von der Webseite
                    // Auth-Nachrichten nicht vollständig loggen (enthält Token)
                    if (message.Contains("\"type\":\"auth\""))
                    {
                        OnLog?.Invoke("Nachricht empfangen: [auth - Token ausgeblendet]");
                    }
                    else
                    {
                        OnLog?.Invoke($"Nachricht empfangen: {message}");
                    }
                    HandleClientMessage(socket, message);
                };
            });

            OnLog?.Invoke($"WebSocket-Server gestartet auf Port {port}");
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Server-Startfehler: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Stoppt den WebSocket-Server
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

        OnLog?.Invoke("WebSocket-Server gestoppt");
    }

    /// <summary>
    /// Sendet Sim-Daten an alle verbundenen Clients
    /// </summary>
    public void BroadcastSimData(SimData data)
    {
        var json = JsonConvert.SerializeObject(data, _jsonSettings);
        Broadcast(json);
    }

    /// <summary>
    /// Sendet Landing-Info an alle verbundenen Clients
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
    /// Sendet Sim-Daten an einen einzelnen Client
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
            OnLog?.Invoke($"Send-Fehler: {ex.Message}");
        }
    }

    /// <summary>
    /// Sendet eine Nachricht an alle verbundenen Clients
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
                OnLog?.Invoke($"Broadcast-Fehler: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Verarbeitet Nachrichten vom Client
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
            // Ungültige Nachricht ignorieren
        }
    }

    /// <summary>
    /// Verarbeitet Auth-Nachrichten vom Frontend (User-ID + Token für Flight-Logging)
    /// </summary>
    private void HandleAuthMessage(ClientCommand command)
    {
        try
        {
            var userId = command.Data?.ToString();
            var token = command.Token;
            OnLog?.Invoke($"Auth empfangen: User-ID = {(string.IsNullOrEmpty(userId) ? "(abgemeldet)" : userId)}");
            OnUserAuthenticated?.Invoke(
                string.IsNullOrEmpty(userId) ? null : userId,
                string.IsNullOrEmpty(token) ? null : token
            );
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Auth-Fehler: {ex.Message}");
        }
    }

    /// <summary>
    /// Verarbeitet Route-Nachrichten und synchronisiert zwischen Clients
    /// </summary>
    private void HandleRouteMessage(IWebSocketConnection sender, ClientCommand command)
    {
        try
        {
            // Route-Daten aus dem Command extrahieren
            var routeJson = JsonConvert.SerializeObject(command.Data);
            var route = JsonConvert.DeserializeObject<RouteData>(routeJson);

            if (route != null)
            {
                _currentRoute = route;
                OnLog?.Invoke($"Route empfangen: {route.Origin} → {route.Destination}");

                // Event für FlightTracker auslösen
                OnRouteReceived?.Invoke(route.Origin, route.Destination);

                // Route an alle Clients broadcasten (inkl. Sender für Bestätigung)
                var routeMessage = JsonConvert.SerializeObject(new { type = "route", route = route }, _jsonSettings);
                Broadcast(routeMessage);
            }
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Route-Fehler: {ex.Message}");
        }
    }

    /// <summary>
    /// Verarbeitet Airport-Anfragen und holt Koordinaten von der API
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

            // Aus Cache holen wenn vorhanden
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

            // Von API laden
            OnLog?.Invoke($"Lade Flughafen von API: {icao}");
            var url = $"https://airport-data.com/api/ap_info.json?icao={icao}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                OnLog?.Invoke($"Airport API Fehler für {icao}: {response.StatusCode}");
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

                // Im Cache speichern
                _airportCache[icao] = coords;
                OnLog?.Invoke($"Flughafen geladen: {icao} ({coords.Lat:F4}, {coords.Lon:F4})");

                // An Client senden
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
                OnLog?.Invoke($"Flughafen nicht gefunden: {icao}");
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
            OnLog?.Invoke($"Airport-API Fehler: {ex.Message}");
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
/// Datenmodell für Client-Befehle
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
/// Datenmodell für Flugrouten
/// </summary>
public class RouteData
{
    [JsonProperty("origin")]
    public string? Origin { get; set; }

    [JsonProperty("destination")]
    public string? Destination { get; set; }
}

/// <summary>
/// Datenmodell für Flughafen-Koordinaten
/// </summary>
public class AirportCoords
{
    public double Lat { get; set; }
    public double Lon { get; set; }
}
