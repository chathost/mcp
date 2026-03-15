using ChatHost.Mcp.Services;
using ChatHost.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
// MCP stdio spec: stdout MUST only contain valid JSON-RPC messages.
// LogToStandardErrorThreshold = Trace ensures ALL log levels go to stderr,
// keeping stdout clean. Using Warning or higher would leak Info/Debug logs to stdout,
// which clients like Cursor reject as invalid JSON.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.AddProvider(new FileLoggerProvider(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".chathost", "logs", $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log")));

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "chathost", Version = "1.0.0" };
        options.ServerInstructions = "chathost.io — deploy websites and apps instantly from your editor. "
            + "Tools are organized into groups: "
            + "account-* (login, logout, whoami) for authentication, "
            + "projects-* (init, this, list, delete) for managing projects, "
            + "deployments-* (deploy, list, status, delete) for managing deployments. "
            + "To deploy: 1) create a Dockerfile, 2) call projects-init if not already initialized, 3) call deployments-deploy with the Dockerfile path. "
            + "Each deployment has a 'slot' (default: 'dev'). Redeploying to the same slot updates the site at the same URL. "
            + "Use different slots (e.g. 'dev', 'live') for separate environments with their own URLs.";
    })
    .WithStdioServerTransport()
    .WithTools<AccountTools>()
    .WithTools<ProjectTools>()
    .WithTools<DeploymentTools>();

builder.Services.AddHttpClient("ChatHostApi", client =>
{
    client.BaseAddress = new Uri(
        Environment.GetEnvironmentVariable("CHATHOST_API_URL")
            ?? "https://api.chathost.io");
});

builder.Services.AddSingleton<AuthService>();

await builder.Build().RunAsync();
