using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatHost.Mcp.Services;

namespace ChatHost.Mcp.Tools;

public record ProjectConfig(string ProjectId, string ProjectName, Dictionary<string, string>? Slots = null);

[JsonSerializable(typeof(ProjectConfig))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class ChatHostJsonContext : JsonSerializerContext;

public static class ToolHelpers
{
    public const string ConfigFileName = "chathost.config";

    public static async Task<string> ExecuteTool(
        AuthService auth,
        IHttpClientFactory httpClientFactory,
        string toolName,
        Func<Task<string>> action)
    {
        try
        {
            return await action();
        }
        catch (AuthRequiredException ex)
        {
            return FormatAuthInstructions(ex.ConnectUrl);
        }
        catch (Exception ex)
        {
            ReportError(auth, httpClientFactory, toolName, ex);
            return $"Error: {ex.Message}";
        }
    }

    public static ProjectConfig? ReadProjectConfig()
    {
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName);
        if (!File.Exists(configPath))
            return null;

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize(json, ChatHostJsonContext.Default.ProjectConfig);
    }

    public static async Task<(int ExitCode, string Output, string Error)> RunCommand(
        string fileName, string arguments, string? stdinInput = null,
        Dictionary<string, string>? environmentVariables = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdinInput != null,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (environmentVariables != null)
            {
                foreach (var (key, value) in environmentVariables)
                    psi.Environment[key] = value;
            }

            using var process = Process.Start(psi)!;

            if (stdinInput != null)
            {
                await process.StandardInput.WriteAsync(stdinInput);
                process.StandardInput.Close();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    public static string FormatAuthInstructions(string connectUrl)
    {
        return "To get started with chathost.io, please sign in by opening this URL in your browser:\n\n"
             + $"  {connectUrl}\n\n"
             + "Once signed in, run this command again.";
    }

    public static void ReportError(
        AuthService auth,
        IHttpClientFactory httpClientFactory,
        string toolName,
        Exception ex)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var token = auth.GetCachedToken();
                if (token == null) return;

                var client = httpClientFactory.CreateClient("ChatHostApi");
                var body = $"{{\"toolName\":{JsonSerializer.Serialize(toolName, ChatHostJsonContext.Default.String)},"
                    + $"\"exceptionType\":{JsonSerializer.Serialize(ex.GetType().Name, ChatHostJsonContext.Default.String)},"
                    + $"\"message\":{JsonSerializer.Serialize(ex.Message, ChatHostJsonContext.Default.String)},"
                    + $"\"stackTrace\":{JsonSerializer.Serialize(ex.StackTrace, ChatHostJsonContext.Default.String)}}}";
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/mcp/telemetry")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                await client.SendAsync(request);
            }
            catch
            {
                // Telemetry is best-effort — silently swallow failures
            }
        });
    }

    public static async Task<string> CallApi(
        AuthService auth,
        IHttpClientFactory httpClientFactory,
        HttpMethod method,
        string path,
        Func<string, string> formatResponse,
        string? jsonBody = null)
    {
        var token = await auth.EnsureAuthenticatedAsync();
        var client = httpClientFactory.CreateClient("ChatHostApi");

        HttpResponseMessage response;
        try
        {
            response = await SendRequest(client, path, token, method, jsonBody);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Could not reach the chathost.io API. Check your internet connection.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            auth.ClearToken();
            token = await auth.EnsureAuthenticatedAsync();
            try
            {
                response = await SendRequest(client, path, token, method, jsonBody);
            }
            catch (HttpRequestException)
            {
                throw new InvalidOperationException("Could not reach the chathost.io API. Check your internet connection.");
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            return $"Error ({response.StatusCode}): {body}";
        }

        var content = await response.Content.ReadAsStringAsync();
        return formatResponse(content);
    }

    public static Task<string> CallApi(
        AuthService auth,
        IHttpClientFactory httpClientFactory,
        string path,
        Func<string, string> formatResponse)
        => CallApi(auth, httpClientFactory, HttpMethod.Get, path, formatResponse);

    public static Task<string> CallApiPost(
        AuthService auth,
        IHttpClientFactory httpClientFactory,
        string path,
        string jsonBody,
        Func<string, string> formatResponse)
        => CallApi(auth, httpClientFactory, HttpMethod.Post, path, formatResponse, jsonBody);

    public static Task<string> CallApiDelete(
        AuthService auth,
        IHttpClientFactory httpClientFactory,
        string path,
        Func<string, string> formatResponse)
        => CallApi(auth, httpClientFactory, HttpMethod.Delete, path, formatResponse);

    private static async Task<HttpResponseMessage> SendRequest(
        HttpClient client, string path, string token, HttpMethod method, string? jsonBody = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (jsonBody != null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        return await client.SendAsync(request);
    }
}
