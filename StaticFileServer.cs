using System.Net;
using System.Text;

namespace MSFSBridge;

/// <summary>
/// Simple HTTP server for static files (website for tablets)
/// </summary>
public class StaticFileServer : IDisposable
{
    private HttpListener? _listener;
    private readonly string _wwwRoot;
    private bool _running = false;
    private Thread? _listenerThread;
    private bool _disposed = false;

    public event Action<string>? OnLog;

    // MIME types for common files
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
    /// Starts the HTTP server
    /// </summary>
    public void Start(int port = 8081)
    {
        if (!Directory.Exists(_wwwRoot))
        {
            OnLog?.Invoke($"No www folder found - HTTP server for tablets disabled");
            OnLog?.Invoke("(Use https://simchecklist.app in your browser instead)");
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

            OnLog?.Invoke($"HTTP server started on port {port}");
            OnLog?.Invoke($"WWW root: {_wwwRoot}");
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5)
        {
            // Access denied - try without + (localhost only)
            OnLog?.Invoke("Admin rights missing for network access, trying localhost...");
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

                OnLog?.Invoke($"HTTP server started on http://localhost:{port} (local only)");
                OnLog?.Invoke("Run as administrator for network access.");
            }
            catch (Exception ex2)
            {
                OnLog?.Invoke($"HTTP server could not be started: {ex2.Message}");
            }
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"HTTP server could not be started: {ex.Message}");
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
                // Server was stopped
                break;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Request error: {ex.Message}");
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Get URL path
            var urlPath = request.Url?.LocalPath ?? "/";
            if (urlPath == "/") urlPath = "/index.html";

            // Security check: No path traversal allowed
            var relativePath = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var filePath = Path.GetFullPath(Path.Combine(_wwwRoot, relativePath));

            if (!filePath.StartsWith(_wwwRoot))
            {
                // Attempt to escape WWW root
                response.StatusCode = 403;
                response.Close();
                return;
            }

            // SPA fallback: If file doesn't exist, serve index.html
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

            // Read and send file
            var fileBytes = File.ReadAllBytes(filePath);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var mimeType = MimeTypes.GetValueOrDefault(extension, "application/octet-stream");

            response.ContentType = mimeType;
            response.ContentLength64 = fileBytes.Length;

            // Cache headers for assets
            if (extension is ".js" or ".css" or ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".woff" or ".woff2")
            {
                response.Headers.Add("Cache-Control", "public, max-age=31536000, immutable");
            }

            // Allow CORS (for WebSocket connection)
            response.Headers.Add("Access-Control-Allow-Origin", "*");

            response.OutputStream.Write(fileBytes, 0, fileBytes.Length);
            response.Close();
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Response error: {ex.Message}");
            try
            {
                response.StatusCode = 500;
                response.Close();
            }
            catch { }
        }
    }

    /// <summary>
    /// Stops the HTTP server
    /// </summary>
    public void Stop()
    {
        _running = false;
        _listener?.Stop();
        _listener?.Close();
        _listenerThread?.Join(1000);
        OnLog?.Invoke("HTTP server stopped");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
