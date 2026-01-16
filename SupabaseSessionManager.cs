using System.Net.WebSockets;
using System.Text;
using MSFSBridge.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MSFSBridge;

/// <summary>
/// Verwaltet die Supabase Realtime Session für Remote-Zugriff
/// </summary>
public class SupabaseSessionManager : IDisposable
{
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cancellationTokenSource;
    private string? _sessionCode;
    private string? _supabaseUrl;
    private string? _supabaseKey;
    private bool _isConnected;
    private bool _disposed;
    private Task? _receiveTask;

    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    public event Action<string>? OnLog;
    public event Action<string>? OnError;

    public string? SessionCode => _sessionCode;
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Prüft ob Supabase konfiguriert ist
    /// </summary>
    public static bool IsConfigured()
    {
        var (url, key) = GetSupabaseCredentials();
        return !string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(key);
    }

    /// <summary>
    /// Holt die Supabase-Zugangsdaten (unterstützt beide Varianten: mit und ohne VITE_ Prefix)
    /// </summary>
    private static (string? url, string? key) GetSupabaseCredentials()
    {
        // Zuerst ohne VITE_ Prefix versuchen, dann mit
        var url = Environment.GetEnvironmentVariable("SUPABASE_URL")
                  ?? Environment.GetEnvironmentVariable("VITE_SUPABASE_URL");
        var key = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY")
                  ?? Environment.GetEnvironmentVariable("VITE_SUPABASE_ANON_KEY");
        return (url, key);
    }

    /// <summary>
    /// Generiert einen zufälligen Session-Code (Format: XXXX-XXXX)
    /// </summary>
    private static string GenerateSessionCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        var code = new char[9];

        for (int i = 0; i < 9; i++)
        {
            if (i == 4)
            {
                code[i] = '-';
            }
            else
            {
                code[i] = chars[random.Next(chars.Length)];
            }
        }

        return new string(code);
    }

    /// <summary>
    /// Startet eine neue Session und verbindet mit Supabase Realtime
    /// </summary>
    public async Task<string?> StartSessionAsync()
    {
        var (url, key) = GetSupabaseCredentials();
        _supabaseUrl = url;
        _supabaseKey = key;

        if (string.IsNullOrEmpty(_supabaseUrl) || string.IsNullOrEmpty(_supabaseKey))
        {
            OnLog?.Invoke("Supabase nicht konfiguriert. Setze SUPABASE_URL und SUPABASE_ANON_KEY in der .env Datei.");
            return null;
        }

        _sessionCode = GenerateSessionCode();
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            await ConnectToRealtimeAsync();
            OnLog?.Invoke($"Session gestartet: {_sessionCode}");
            return _sessionCode;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Fehler beim Starten der Session: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Verbindet mit dem Supabase Realtime WebSocket
    /// </summary>
    private async Task ConnectToRealtimeAsync()
    {
        _webSocket = new ClientWebSocket();

        // Supabase Realtime URL konstruieren
        var realtimeUrl = _supabaseUrl!
            .Replace("https://", "wss://")
            .Replace("http://", "ws://");

        if (!realtimeUrl.EndsWith("/"))
            realtimeUrl += "/";

        realtimeUrl += $"realtime/v1/websocket?apikey={_supabaseKey}&vsn=1.0.0";

        OnLog?.Invoke($"Verbinde mit Supabase Realtime...");

        await _webSocket.ConnectAsync(new Uri(realtimeUrl), _cancellationTokenSource!.Token);

        _isConnected = true;
        OnLog?.Invoke("Mit Supabase Realtime verbunden");

        // Receive-Task starten für Heartbeat
        _receiveTask = Task.Run(ReceiveLoopAsync);

        // Channel beitreten
        await JoinChannelAsync();
    }

    /// <summary>
    /// Tritt dem Session-Channel bei
    /// </summary>
    private async Task JoinChannelAsync()
    {
        var joinMessage = new
        {
            topic = $"realtime:session:{_sessionCode}",
            @event = "phx_join",
            payload = new { config = new { broadcast = new { self = false } } },
            @ref = "1"
        };

        await SendMessageAsync(joinMessage);
        OnLog?.Invoke($"Channel session:{_sessionCode} beigetreten");
    }

    /// <summary>
    /// Empfangsschleife für WebSocket (Heartbeat etc.)
    /// </summary>
    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[4096];

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !_cancellationTokenSource!.Token.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    _cancellationTokenSource.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    OnLog?.Invoke("Supabase Realtime Verbindung geschlossen");
                    _isConnected = false;
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    // Heartbeat beantworten
                    if (message.Contains("\"event\":\"heartbeat\"") || message.Contains("phx_reply"))
                    {
                        // Heartbeat-Reply senden
                        var heartbeatReply = new
                        {
                            topic = "phoenix",
                            @event = "heartbeat",
                            payload = new { },
                            @ref = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
                        };
                        await SendMessageAsync(heartbeatReply);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal beim Beenden
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"WebSocket-Empfangsfehler: {ex.Message}");
            _isConnected = false;
        }
    }

    /// <summary>
    /// Sendet SimData an alle verbundenen Clients über Supabase Realtime
    /// </summary>
    public async Task BroadcastSimDataAsync(SimData data)
    {
        if (_webSocket?.State != WebSocketState.Open || string.IsNullOrEmpty(_sessionCode))
            return;

        try
        {
            var broadcastMessage = new
            {
                topic = $"realtime:session:{_sessionCode}",
                @event = "broadcast",
                payload = new
                {
                    type = "broadcast",
                    @event = "simdata",
                    payload = data
                },
                @ref = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
            };

            await SendMessageAsync(broadcastMessage);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Broadcast-Fehler: {ex.Message}");
        }
    }

    /// <summary>
    /// Sendet eine Nachricht über den WebSocket
    /// </summary>
    private async Task SendMessageAsync(object message)
    {
        if (_webSocket?.State != WebSocketState.Open)
            return;

        var json = JsonConvert.SerializeObject(message, _jsonSettings);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            _cancellationTokenSource!.Token);
    }

    /// <summary>
    /// Stoppt die Session
    /// </summary>
    public async Task StopSessionAsync()
    {
        OnLog?.Invoke("Session wird beendet...");

        _cancellationTokenSource?.Cancel();

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session beendet", CancellationToken.None);
            }
            catch { }
        }

        _isConnected = false;
        _sessionCode = null;

        OnLog?.Invoke("Session beendet");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cancellationTokenSource?.Cancel();
        _webSocket?.Dispose();
        _cancellationTokenSource?.Dispose();

        GC.SuppressFinalize(this);
    }
}
