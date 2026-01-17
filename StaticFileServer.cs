using System.Net;
using System.Text;

namespace MSFSBridge;

/// <summary>
/// Einfacher HTTP-Server für statische Dateien (Website für Tablets)
/// </summary>
public class StaticFileServer : IDisposable
{
    private HttpListener? _listener;
    private readonly string _wwwRoot;
    private bool _running = false;
    private Thread? _listenerThread;
    private bool _disposed = false;

    public event Action<string>? OnLog;

    // MIME-Types für gängige Dateien
    private static readonly Dictionary<string, string> MimeTypes = new()
    {
        { ".html", "text/html; charset=utf-8" },
        { ".htm", "text/html; charset=utf-8" },
        { ".css", "text/css; charset=utf-8" },
        { ".js", "application/javascript; charset=utf-8" },
        { ".json", "application/json; charset=utf-8" },
        { ".png", "image/png" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".gif", "image/gif" },
        { ".svg", "image/svg+xml" },
        { ".ico", "image/x-icon" },
        { ".woff", "font/woff" },
        { ".woff2", "font/woff2" },
        { ".ttf", "font/ttf" },
        { ".eot", "application/vnd.ms-fontobject" }
    };

    public StaticFileServer(string wwwRoot)
    {
        _wwwRoot = Path.GetFullPath(wwwRoot);
    }

    /// <summary>
    /// Startet den HTTP-Server
    /// </summary>
    public void Start(int port = 8081)
    {
        if (!Directory.Exists(_wwwRoot))
        {
            OnLog?.Invoke($"Kein www-Ordner gefunden - HTTP-Server für Tablets deaktiviert");
            OnLog?.Invoke("(Nutze https://simchecklist.app im Browser stattdessen)");
            return;
        }

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{port}/");
            _listener.Start();
            _running = true;

            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "StaticFileServer"
            };
            _listenerThread.Start();

            OnLog?.Invoke($"HTTP-Server gestartet auf Port {port}");
            OnLog?.Invoke($"WWW-Root: {_wwwRoot}");
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5)
        {
            // Access denied - versuche ohne + (nur localhost)
            OnLog?.Invoke("Admin-Rechte fehlen für Netzwerk-Zugriff, versuche localhost...");
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                _running = true;

                _listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "StaticFileServer"
                };
                _listenerThread.Start();

                OnLog?.Invoke($"HTTP-Server gestartet auf http://localhost:{port} (nur lokal)");
                OnLog?.Invoke("Für Netzwerk-Zugriff als Administrator starten.");
            }
            catch (Exception ex2)
            {
                OnLog?.Invoke($"HTTP-Server konnte nicht gestartet werden: {ex2.Message}");
            }
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"HTTP-Server konnte nicht gestartet werden: {ex.Message}");
        }
    }

    private void ListenLoop()
    {
        while (_running && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = _listener.GetContext();
                ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
            }
            catch (HttpListenerException)
            {
                // Server wurde gestoppt
                break;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Request-Fehler: {ex.Message}");
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // URL-Pfad ermitteln
            var urlPath = request.Url?.LocalPath ?? "/";
            if (urlPath == "/") urlPath = "/index.html";

            // Sicherheitscheck: Keine Pfad-Traversal erlauben
            var relativePath = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var filePath = Path.GetFullPath(Path.Combine(_wwwRoot, relativePath));

            if (!filePath.StartsWith(_wwwRoot))
            {
                // Versuch, aus dem WWW-Root auszubrechen
                response.StatusCode = 403;
                response.Close();
                return;
            }

            // SPA-Fallback: Wenn Datei nicht existiert, index.html liefern
            if (!File.Exists(filePath))
            {
                var indexPath = Path.Combine(_wwwRoot, "index.html");
                if (File.Exists(indexPath))
                {
                    filePath = indexPath;
                }
                else
                {
                    response.StatusCode = 404;
                    var errorBytes = Encoding.UTF8.GetBytes("404 - Not Found");
                    response.ContentType = "text/plain";
                    response.ContentLength64 = errorBytes.Length;
                    response.OutputStream.Write(errorBytes, 0, errorBytes.Length);
                    response.Close();
                    return;
                }
            }

            // Datei lesen und senden
            var fileBytes = File.ReadAllBytes(filePath);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var mimeType = MimeTypes.GetValueOrDefault(extension, "application/octet-stream");

            response.ContentType = mimeType;
            response.ContentLength64 = fileBytes.Length;

            // Cache-Header für Assets
            if (extension is ".js" or ".css" or ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".woff" or ".woff2")
            {
                response.Headers.Add("Cache-Control", "public, max-age=31536000, immutable");
            }

            // CORS erlauben (für WebSocket-Verbindung)
            response.Headers.Add("Access-Control-Allow-Origin", "*");

            response.OutputStream.Write(fileBytes, 0, fileBytes.Length);
            response.Close();
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Response-Fehler: {ex.Message}");
            try
            {
                response.StatusCode = 500;
                response.Close();
            }
            catch { }
        }
    }

    /// <summary>
    /// Stoppt den HTTP-Server
    /// </summary>
    public void Stop()
    {
        _running = false;
        _listener?.Stop();
        _listener?.Close();
        _listenerThread?.Join(1000);
        OnLog?.Invoke("HTTP-Server gestoppt");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
