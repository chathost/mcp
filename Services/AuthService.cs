using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatHost.Mcp.Services;

public class AuthRequiredException(string connectUrl) : Exception("Authentication required")
{
    public string ConnectUrl { get; } = connectUrl;
}

public class AuthService
{
    private static readonly string CredentialsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".chathost");
    private static readonly string CredentialsFile = Path.Combine(CredentialsDir, "credentials.json");

    private static readonly string WebsiteUrl =
        Environment.GetEnvironmentVariable("CHATHOST_WEBSITE_URL")
            ?? "https://chathost.io";

    private string? _cachedToken;

    public async Task<string> EnsureAuthenticatedAsync()
    {
        // Return cached token
        if (_cachedToken != null)
            return _cachedToken;

        // Try loading from disk
        var saved = LoadSavedToken();
        if (saved != null)
        {
            _cachedToken = saved;
            return saved;
        }

        // Run browser auth flow
        var token = await RunBrowserAuthFlowAsync();
        SaveToken(token);
        _cachedToken = token;
        return token;
    }

    public string? GetCachedToken() => _cachedToken ?? LoadSavedToken();

    public void ClearToken()
    {
        _cachedToken = null;
        if (File.Exists(CredentialsFile))
            File.Delete(CredentialsFile);
    }

    private static string? LoadSavedToken()
    {
        if (!File.Exists(CredentialsFile))
            return null;

        try
        {
            var json = File.ReadAllText(CredentialsFile);
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("token").GetString();
        }
        catch
        {
            return null;
        }
    }

    private static void SaveToken(string token)
    {
        Directory.CreateDirectory(CredentialsDir);
        var data = JsonSerializer.Serialize(
            new CredentialsData { Token = token, CreatedAt = DateTime.UtcNow.ToString("o") },
            CredentialsJsonContext.Default.CredentialsData);
        File.WriteAllText(CredentialsFile, data);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(CredentialsFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static async Task<string> RunBrowserAuthFlowAsync()
    {
        // Find an available port
        using var tempListener = new HttpListener();
        var port = FindAvailablePort();
        var prefix = $"http://localhost:{port}/";
        tempListener.Stop();

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        // Open browser with CSRF state parameter
        var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var connectUrl = $"{WebsiteUrl}/connect?callback_port={port}&state={state}";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = connectUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Browser failed to open (common in WSL2/headless environments).
            // Keep the listener running in the background so if the user manually
            // visits the URL, the token still gets saved.
            _ = Task.Run(async () =>
            {
                try
                {
                    using var bgCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    var ctx = await listener.GetContextAsync().WaitAsync(bgCts.Token);
                    var token = ctx.Request.QueryString["token"];
                    var callbackState = ctx.Request.QueryString["state"];
                    if (!string.IsNullOrEmpty(token) && callbackState == state)
                    {
                        SaveToken(token);
                        RespondHtml(ctx, "Connected to chathost.io! You can close this tab.");
                    }
                    else
                    {
                        RespondHtml(ctx, "Authentication failed. Please try again.");
                    }
                }
                catch
                {
                    // Listener timed out or errored — nothing to do
                }
                finally
                {
                    listener.Stop();
                }
            });

            throw new AuthRequiredException(connectUrl);
        }

        // Wait for callback with timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            var contextTask = listener.GetContextAsync();
            var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cts.Token));

            if (completed != contextTask)
                throw new TimeoutException("Authentication timed out after 5 minutes.");

            var context = await contextTask;
            var callbackState = context.Request.QueryString["state"];
            if (callbackState != state)
            {
                RespondHtml(context, "Authentication failed — invalid state parameter. Please try again.");
                throw new InvalidOperationException("CSRF state mismatch in callback.");
            }

            var token = context.Request.QueryString["token"];

            if (string.IsNullOrEmpty(token))
            {
                RespondHtml(context, "Authentication failed — no token received. Please try again.");
                throw new InvalidOperationException("No token received in callback.");
            }

            RespondHtml(context, "Connected to chathost.io! You can close this tab.");
            return token;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static int FindAvailablePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void RespondHtml(HttpListenerContext context, string message)
    {
        var html = $"""
            <!DOCTYPE html>
            <html>
            <head><title>chathost.io</title></head>
            <body style="font-family: system-ui, sans-serif; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0;">
                <div style="text-align: center;">
                    <h1>{message}</h1>
                </div>
            </body>
            </html>
            """;

        var buffer = System.Text.Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.Close();
    }

}

internal class CredentialsData
{
    [JsonPropertyName("token")]
    public required string Token { get; set; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; set; }
}

[JsonSerializable(typeof(CredentialsData))]
internal partial class CredentialsJsonContext : JsonSerializerContext
{
}
