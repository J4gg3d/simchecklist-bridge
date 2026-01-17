using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MSFSBridge;

public class UpdateChecker
{
    public const string CURRENT_VERSION = "1.6.1";
    private const string GITHUB_REPO = "J4gg3d/simchecklist-bridge";
    private const string GITHUB_API = $"https://api.github.com/repos/{GITHUB_REPO}/releases/latest";

    private readonly HttpClient _httpClient;

    public UpdateChecker()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "MSFSBridge-UpdateChecker");
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public async Task<(bool updateAvailable, GitHubRelease? release)> CheckForUpdateAsync()
    {
        try
        {
            var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(GITHUB_API);
            if (release == null) return (false, null);

            var latestVersion = release.TagName.TrimStart('v', 'V');
            var isNewer = CompareVersions(latestVersion, CURRENT_VERSION) > 0;

            return (isNewer, release);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UPDATE] Fehler beim Prüfen auf Updates: {ex.Message}");
            return (false, null);
        }
    }

    private int CompareVersions(string v1, string v2)
    {
        var parts1 = v1.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var parts2 = v2.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

        for (int i = 0; i < Math.Max(parts1.Length, parts2.Length); i++)
        {
            var p1 = i < parts1.Length ? parts1[i] : 0;
            var p2 = i < parts2.Length ? parts2[i] : 0;
            if (p1 != p2) return p1.CompareTo(p2);
        }
        return 0;
    }

    public async Task<bool> DownloadAndInstallUpdateAsync(GitHubRelease release)
    {
        // Find ZIP asset
        var zipAsset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (zipAsset == null)
        {
            Console.WriteLine("[UPDATE] Keine ZIP-Datei im Release gefunden.");
            return false;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "MSFSBridge_Update");
        var zipPath = Path.Combine(tempDir, zipAsset.Name);
        var extractPath = Path.Combine(tempDir, "extracted");

        try
        {
            // Cleanup temp directory
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(extractPath);

            // Download ZIP
            Console.WriteLine($"[UPDATE] Lade herunter: {zipAsset.Name} ({zipAsset.Size / 1024 / 1024:F1} MB)...");

            using (var response = await _httpClient.GetAsync(zipAsset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? zipAsset.Size;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                var totalRead = 0L;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    var progress = (double)totalRead / totalBytes * 100;
                    Console.Write($"\r[UPDATE] Download: {progress:F0}%   ");
                }
                Console.WriteLine();
            }

            // Extract ZIP
            Console.WriteLine("[UPDATE] Entpacke Update...");
            ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);

            // Find the actual files (might be in a subfolder)
            var sourceDir = extractPath;
            var subDirs = Directory.GetDirectories(extractPath);
            if (subDirs.Length == 1 && !File.Exists(Path.Combine(extractPath, "MSFSBridge.exe")))
            {
                sourceDir = subDirs[0];
            }

            // Create update batch script
            var currentDir = AppContext.BaseDirectory;
            var batchPath = Path.Combine(tempDir, "update.bat");
            var batchContent = $@"@echo off
echo.
echo ========================================
echo   MSFSBridge Update wird installiert...
echo ========================================
echo.

:: Wait for the application to close
timeout /t 2 /nobreak > nul

:: Copy new files
echo Kopiere neue Dateien...
xcopy /s /y ""{sourceDir}\*"" ""{currentDir}""

:: Cleanup
echo Raeume auf...
rmdir /s /q ""{tempDir}""

:: Restart the application
echo.
echo Update abgeschlossen! Starte Bridge neu...
echo.
timeout /t 2 /nobreak > nul

cd /d ""{currentDir}""
if exist ""MSFSBridge.exe"" (
    start """" ""MSFSBridge.exe""
) else (
    start """" ""start-bridge.bat""
)

exit
";
            await File.WriteAllTextAsync(batchPath, batchContent);

            Console.WriteLine("[UPDATE] Update bereit. Die Bridge wird jetzt neu gestartet...");
            Console.WriteLine();

            // Start the update script and exit
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchPath}\"",
                UseShellExecute = true,
                CreateNoWindow = false
            };
            Process.Start(psi);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UPDATE] Fehler beim Update: {ex.Message}");
            return false;
        }
    }

    public static async Task CheckAndPromptForUpdateAsync()
    {
        var checker = new UpdateChecker();
        var (updateAvailable, release) = await checker.CheckForUpdateAsync();

        if (!updateAvailable || release == null)
        {
            Console.WriteLine($"[UPDATE] Version {CURRENT_VERSION} ist aktuell.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    UPDATE VERFÜGBAR!                         ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"  Aktuelle Version: {CURRENT_VERSION}");
        Console.WriteLine($"  Neue Version:     {release.TagName}");
        Console.WriteLine();

        if (!string.IsNullOrWhiteSpace(release.Body))
        {
            Console.WriteLine("  Änderungen:");
            foreach (var line in release.Body.Split('\n').Take(5))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    Console.WriteLine($"    {trimmed}");
            }
            Console.WriteLine();
        }

        Console.WriteLine("  [J] Jetzt aktualisieren");
        Console.WriteLine("  [N] Später (Bridge normal starten)");
        Console.WriteLine("  [I] Im Browser öffnen");
        Console.WriteLine();
        Console.Write("  Deine Wahl: ");

        var key = Console.ReadKey();
        Console.WriteLine();
        Console.WriteLine();

        switch (char.ToUpper(key.KeyChar))
        {
            case 'J':
                var success = await checker.DownloadAndInstallUpdateAsync(release);
                if (success)
                {
                    Environment.Exit(0);
                }
                break;

            case 'I':
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = release.HtmlUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
                Console.WriteLine("[UPDATE] Release-Seite im Browser geöffnet.");
                Console.WriteLine();
                break;

            default:
                Console.WriteLine("[UPDATE] Update übersprungen. Starte Bridge...");
                Console.WriteLine();
                break;
        }
    }
}
