using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChatHost.Mcp.Services;
using ModelContextProtocol.Server;

namespace ChatHost.Mcp.Tools;

[McpServerToolType]
public class DeploymentTools
{
    private static readonly Regex SlotNameRegex = new(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);

    /// <summary>
    /// On Linux (including WSL), explicitly set DOCKER_HOST to the Unix socket
    /// to prevent WSL interop from routing Docker commands to the Windows named pipe.
    /// </summary>
    private static Dictionary<string, string>? GetDockerEnvironment()
    {
        if (!OperatingSystem.IsLinux())
            return null;
        if (Environment.GetEnvironmentVariable("DOCKER_HOST") != null)
            return null;
        if (File.Exists("/var/run/docker.sock"))
            return new Dictionary<string, string> { ["DOCKER_HOST"] = "unix:///var/run/docker.sock" };
        return null;
    }

    [McpServerTool(
        Name = "deployments-list",
        Title = "List Deployments",
        ReadOnly = true,
        OpenWorld = false),
     Description("List all your deployments across all projects. Returns each deployment grouped by project, showing: slot name (e.g. 'dev', 'live'), status (Running/Stopped/Failed), live URL, port mappings, and creation date. Use this to see all your hosted sites and their current status.")]
    public Task<string> ListDeployments(
        AuthService auth,
        IHttpClientFactory httpClientFactory)
    {
        return ToolHelpers.ExecuteTool(auth, httpClientFactory, "deployments-list", () =>
            ToolHelpers.CallApi(auth, httpClientFactory, "api/mcp/deployments", response =>
            {
                using var doc = JsonDocument.Parse(response);
                var deployments = doc.RootElement;

                if (deployments.GetArrayLength() == 0)
                    return "You have no deployments yet. Use deployments-deploy to deploy your first app!";

                var sb = new StringBuilder("Your deployments:\n");
                string? currentProject = null;

                foreach (var d in deployments.EnumerateArray())
                {
                    var projectName = d.GetProperty("projectName").GetString();
                    var slot = d.GetProperty("slot").GetString();
                    var status = d.GetProperty("status").GetString();
                    var url = d.GetProperty("url").GetString();
                    var createdAt = d.GetProperty("createdAt").GetString();
                    var id = d.GetProperty("id").GetInt32();

                    if (projectName != currentProject)
                    {
                        currentProject = projectName;
                        sb.Append($"\n  {projectName} (project)\n");
                    }

                    sb.Append($"    {slot,-6} | {status,-8} | {url}\n");

                    if (d.TryGetProperty("ports", out var ports) && ports.GetArrayLength() > 0)
                    {
                        var portParts = new List<string>();
                        foreach (var p in ports.EnumerateArray())
                        {
                            var containerPort = p.GetProperty("containerPort").GetInt32();
                            var hostPort = p.GetProperty("hostPort").GetInt32();
                            portParts.Add($"{containerPort} -> {hostPort}");
                        }
                        sb.Append($"             Ports: {string.Join(" | ", portParts)}");
                    }

                    if (createdAt != null)
                    {
                        var date = DateTime.Parse(createdAt).ToString("yyyy-MM-dd");
                        sb.Append($" | Created: {date}");
                    }

                    sb.Append($" | ID: {id}\n");
                }

                return sb.ToString();
            }));
    }

    [McpServerTool(
        Name = "deployments-deploy",
        Title = "Deploy Docker App",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false),
     Description("Build and deploy a Docker application to chathost.io. Requires a Dockerfile path and an optional slot name (defaults to 'dev'). Builds the Docker image locally, pushes it to the chathost registry, and deploys it to a live URL. Redeploying to an existing slot updates the site at the same URL. A project must be initialized first with projects-init. Returns the live URL when deployment is complete.")]
    public async Task<string> DeployDocker(
        AuthService auth,
        IHttpClientFactory httpClientFactory,
        [Description("Path to the Dockerfile")] string dockerfilePath,
        [Description("Slot name for this deployment (lowercase letters, numbers, hyphens only — e.g. 'dev', 'live', 'staging-v2'). Defaults to 'dev'. Deploying to an existing slot updates it at the same URL.")] string? slot = null)
    {
        slot ??= "dev";

        if (slot.Length > 128 || !SlotNameRegex.IsMatch(slot))
            return "Invalid slot name. Use only lowercase letters, numbers, and hyphens (e.g. 'dev', 'live', 'staging-v2'). Must not start or end with a hyphen.";

        var config = ToolHelpers.ReadProjectConfig();
        if (config == null)
            return "No project initialized. Call projects-init first.";

        return await ToolHelpers.ExecuteTool(auth, httpClientFactory, "deployments-deploy", async () =>
        {
            var dockerEnv = GetDockerEnvironment();

            var dockerCheck = await ToolHelpers.RunCommand("docker", "--version", environmentVariables: dockerEnv);
            if (dockerCheck.ExitCode != 0)
                return "Docker is not installed. Please install Docker with BuildKit (buildx) support. "
                     + "Install instructions: https://docs.docker.com/get-docker/ — "
                     + "Docker Desktop includes buildx by default. "
                     + "For Linux without Docker Desktop, also install the buildx plugin: "
                     + "https://docs.docker.com/build/install-buildx/";

            var buildxCheck = await ToolHelpers.RunCommand("docker", "buildx version", environmentVariables: dockerEnv);
            if (buildxCheck.ExitCode != 0)
                return "Docker is installed but the buildx plugin is missing. "
                     + "chathost.io requires Docker buildx to build images. "
                     + "Install instructions: https://docs.docker.com/build/install-buildx/ — "
                     + "If using Docker Desktop, update to the latest version (buildx is included). "
                     + "On Linux, install the plugin: sudo apt-get install docker-buildx-plugin";

            var credsJson = await ToolHelpers.CallApi(auth, httpClientFactory, "api/mcp/registry-credentials", r => r);
            if (credsJson.StartsWith("Error"))
                return $"Failed to get registry credentials: {credsJson}";

            using var credsDoc = JsonDocument.Parse(credsJson);
            var credsRoot = credsDoc.RootElement;
            var registry = credsRoot.GetProperty("registry").GetString()!;
            var imagePrefix = credsRoot.GetProperty("imagePrefix").GetString()!;
            var username = credsRoot.GetProperty("username").GetString()!;
            var password = credsRoot.GetProperty("password").GetString()!;

            var loginCheck = await ToolHelpers.RunCommand("docker", $"login {registry} --get-login", environmentVariables: dockerEnv);
            if (loginCheck.ExitCode != 0)
            {
                var login = await ToolHelpers.RunCommand("docker", $"login {registry} -u {username} --password-stdin", password, dockerEnv);
                if (login.ExitCode != 0)
                    return $"Docker login failed: {login.Error}";
            }

            var dockerfileFullPath = Path.GetFullPath(dockerfilePath);
            if (!File.Exists(dockerfileFullPath))
                return $"Dockerfile not found at: {dockerfileFullPath}";

            var dockerfileContents = await File.ReadAllTextAsync(dockerfileFullPath);

            var imageTag = $"{imagePrefix}/{config.ProjectId}:{slot}";
            var contextDir = Path.GetDirectoryName(dockerfileFullPath)!;
            var build = await ToolHelpers.RunCommand("docker", $"buildx build -f {dockerfileFullPath} -t {imageTag} {contextDir}", environmentVariables: dockerEnv);
            if (build.ExitCode != 0)
                return $"Docker build failed:\n{build.Output}\n{build.Error}";

            var push = await ToolHelpers.RunCommand("docker", $"push {imageTag}", environmentVariables: dockerEnv);
            if (push.ExitCode != 0)
                return $"Docker push failed:\n{push.Output}\n{push.Error}";

            var deployBody = $"{{\"imageReference\":{JsonSerializer.Serialize(imageTag, ChatHostJsonContext.Default.String)},\"dockerfileContents\":{JsonSerializer.Serialize(dockerfileContents, ChatHostJsonContext.Default.String)},\"projectId\":{JsonSerializer.Serialize(config.ProjectId, ChatHostJsonContext.Default.String)},\"slot\":{JsonSerializer.Serialize(slot, ChatHostJsonContext.Default.String)}}}";

            return await ToolHelpers.CallApiPost(auth, httpClientFactory, "api/mcp/deploy-docker",
                deployBody,
                response =>
                {
                    using var doc = JsonDocument.Parse(response);
                    var root = doc.RootElement;

                    var url = root.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                    var redeployed = root.TryGetProperty("redeployed", out var redeployedProp) && redeployedProp.GetBoolean();
                    var slotName = root.TryGetProperty("slot", out var slotProp) ? slotProp.GetString() : slot;

                    if (url != null)
                    {
                        var slots = config.Slots ?? new Dictionary<string, string>();
                        slots[slotName!] = url;
                        var updatedConfig = new ProjectConfig(config.ProjectId, config.ProjectName, slots);
                        var configPath = Path.Combine(Directory.GetCurrentDirectory(), ToolHelpers.ConfigFileName);
                        var configJson = JsonSerializer.Serialize(updatedConfig, ChatHostJsonContext.Default.ProjectConfig);
                        File.WriteAllText(configPath, configJson + "\n");
                    }

                    var action = redeployed ? "Redeployed" : "Deployed";

                    if (url == null)
                        return $"{action}! Response: {response}";

                    return $"{action} to slot '{slotName}'! URL: {url}";
                });
        });
    }

    [McpServerTool(
        Name = "deployments-status",
        Title = "Deployment Status",
        ReadOnly = true,
        OpenWorld = false),
     Description("Get detailed status of a specific deployment by its ID. Returns: project name, slot name, current status (Running/Stopped/Failed), live URL, port mappings (container port to host port with hostname), and creation date. Use this to check if a specific deployment is healthy or to get its connection details.")]
    public Task<string> DeploymentStatus(
        AuthService auth,
        IHttpClientFactory httpClientFactory,
        [Description("The deployment ID")] int deploymentId)
    {
        return ToolHelpers.ExecuteTool(auth, httpClientFactory, "deployments-status", () =>
            ToolHelpers.CallApi(auth, httpClientFactory, $"api/mcp/deployments/{deploymentId}", response =>
            {
                using var doc = JsonDocument.Parse(response);
                var d = doc.RootElement;

                var projectName = d.GetProperty("projectName").GetString();
                var slot = d.GetProperty("slot").GetString();
                var status = d.GetProperty("status").GetString();
                var url = d.GetProperty("url").GetString();
                var hostname = d.GetProperty("hostname").GetString();
                var createdAt = d.GetProperty("createdAt").GetString();
                var id = d.GetProperty("id").GetInt32();

                var sb = new StringBuilder();
                sb.AppendLine($"Deployment #{id}");
                sb.AppendLine($"  Project:  {projectName}");
                sb.AppendLine($"  Slot:     {slot}");
                sb.AppendLine($"  Status:   {status}");
                sb.AppendLine($"  URL:      {url}");
                sb.AppendLine($"  Hostname: {hostname}");

                if (d.TryGetProperty("ports", out var ports) && ports.GetArrayLength() > 0)
                {
                    sb.AppendLine("  Ports:");
                    foreach (var p in ports.EnumerateArray())
                    {
                        var containerPort = p.GetProperty("containerPort").GetInt32();
                        var hostPort = p.GetProperty("hostPort").GetInt32();
                        var portHostname = p.GetProperty("hostname").GetString();
                        sb.AppendLine($"    {containerPort} -> {hostPort} ({portHostname})");
                    }
                }

                if (createdAt != null)
                {
                    var date = DateTime.Parse(createdAt).ToString("yyyy-MM-dd HH:mm UTC");
                    sb.AppendLine($"  Created:  {date}");
                }

                return sb.ToString().TrimEnd();
            }));
    }

    [McpServerTool(
        Name = "deployments-delete",
        Title = "Delete Deployment",
        Destructive = true,
        OpenWorld = false),
     Description("Permanently delete a deployment by its ID. This stops the running container, removes DNS records, and tears down all associated resources including any attached storage volumes. The deployment's URL will stop working immediately. This action cannot be undone. Returns confirmation with the deleted deployment's name, slot, and project.")]
    public Task<string> DeleteDeployment(
        AuthService auth,
        IHttpClientFactory httpClientFactory,
        [Description("The deployment ID to delete")] int deploymentId)
    {
        return ToolHelpers.ExecuteTool(auth, httpClientFactory, "deployments-delete", () =>
            ToolHelpers.CallApiDelete(auth, httpClientFactory, $"api/mcp/deployments/{deploymentId}", response =>
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                var name = root.GetProperty("name").GetString();
                var slot = root.GetProperty("slot").GetString();
                var projectName = root.TryGetProperty("projectName", out var pn) ? pn.GetString() : null;

                var result = $"Deployment deleted.\n  Name: {name}\n  Slot: {slot}";
                if (projectName != null)
                    result += $"\n  Project: {projectName}";

                return result;
            }));
    }
}
