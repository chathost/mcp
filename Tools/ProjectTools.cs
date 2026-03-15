using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ChatHost.Mcp.Services;
using ModelContextProtocol.Server;

namespace ChatHost.Mcp.Tools;

[McpServerToolType]
public class ProjectTools
{
    [McpServerTool(
        Name = "projects-init",
        Title = "Initialize Project",
        Destructive = false,
        OpenWorld = false),
     Description("Initialize the current folder as a chathost.io project. Creates a project on chathost.io and writes a chathost.config file to the current directory. This must be done before deploying. If a chathost.config already exists, it will be overwritten with the new project. Returns the project ID and config file path.")]
    public Task<string> SetProject(
        AuthService auth,
        IHttpClientFactory httpClientFactory,
        [Description("The name for the project")] string name)
    {
        return ToolHelpers.ExecuteTool(auth, httpClientFactory, "projects-init", () =>
        {
            var requestJson = $"{{\"name\":{JsonSerializer.Serialize(name, ChatHostJsonContext.Default.String)}}}";

            return ToolHelpers.CallApiPost(auth, httpClientFactory, "api/mcp/projects",
                requestJson,
                response =>
                {
                    using var doc = JsonDocument.Parse(response);
                    var root = doc.RootElement;
                    var projectId = root.GetProperty("id").GetString();
                    var projectName = root.GetProperty("name").GetString();

                    var configJson = JsonSerializer.Serialize(
                        new ProjectConfig(projectId!, projectName!, new Dictionary<string, string>()),
                        ChatHostJsonContext.Default.ProjectConfig);

                    var configPath = Path.Combine(Directory.GetCurrentDirectory(), ToolHelpers.ConfigFileName);
                    File.WriteAllText(configPath, configJson + "\n");

                    return $"Project created!\n  ID: {projectId}\n  Name: {projectName}\n  Config written to: {configPath}";
                });
        });
    }

    [McpServerTool(
        Name = "projects-this",
        Title = "Current Project",
        ReadOnly = true,
        OpenWorld = false),
     Description("Show the chathost.io project configured in the current directory. Reads the local chathost.config file and returns the project ID, name, config file path, and any deployment slots with their URLs. No authentication or API call required — purely local.")]
    public string GetCurrentProject()
    {
        var config = ToolHelpers.ReadProjectConfig();
        if (config == null)
            return "No project initialized in this directory. Use projects-init to create one.";

        var configPath = Path.Combine(Directory.GetCurrentDirectory(), ToolHelpers.ConfigFileName);
        var sb = new StringBuilder();
        sb.AppendLine($"Project: {config.ProjectName}");
        sb.AppendLine($"  ID:     {config.ProjectId}");
        sb.AppendLine($"  Config: {configPath}");

        if (config.Slots != null && config.Slots.Count > 0)
        {
            sb.AppendLine("  Slots:");
            foreach (var (slot, url) in config.Slots)
                sb.AppendLine($"    {slot} -> {url}");
        }

        return sb.ToString().TrimEnd();
    }

    [McpServerTool(
        Name = "projects-list",
        Title = "List Projects",
        ReadOnly = true,
        OpenWorld = false),
     Description("List all your chathost.io projects. Returns each project's name, ID, number of deployments, and creation date. Use this to see all projects associated with your account.")]
    public Task<string> ListProjects(
        AuthService auth,
        IHttpClientFactory httpClientFactory)
    {
        return ToolHelpers.ExecuteTool(auth, httpClientFactory, "projects-list", () =>
            ToolHelpers.CallApi(auth, httpClientFactory, "api/mcp/projects", response =>
            {
                using var doc = JsonDocument.Parse(response);
                var projects = doc.RootElement;

                if (projects.GetArrayLength() == 0)
                    return "You have no projects yet. Use projects-init to create one!";

                var sb = new StringBuilder("Your projects:\n");
                foreach (var p in projects.EnumerateArray())
                {
                    var name = p.GetProperty("name").GetString();
                    var id = p.GetProperty("id").GetString();
                    var deploymentCount = p.GetProperty("deploymentCount").GetInt32();
                    var createdAt = p.GetProperty("createdAt").GetString();

                    var date = createdAt != null ? DateTime.Parse(createdAt).ToString("yyyy-MM-dd") : "unknown";
                    var deploymentsLabel = deploymentCount == 1 ? "1 deployment" : $"{deploymentCount} deployments";
                    sb.AppendLine($"  {name} | {deploymentsLabel} | Created: {date} | ID: {id}");
                }

                return sb.ToString().TrimEnd();
            }));
    }

    [McpServerTool(
        Name = "projects-delete",
        Title = "Delete Project",
        Destructive = true,
        OpenWorld = false),
     Description("Permanently delete a chathost.io project by its ID. The project must have no active deployments — delete all deployments first using deployments-delete. This action cannot be undone.")]
    public Task<string> DeleteProject(
        AuthService auth,
        IHttpClientFactory httpClientFactory,
        [Description("The project ID (GUID) to delete")] string projectId)
    {
        return ToolHelpers.ExecuteTool(auth, httpClientFactory, "projects-delete", () =>
            ToolHelpers.CallApiDelete(auth, httpClientFactory, $"api/mcp/projects/{projectId}", response =>
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                var name = root.GetProperty("name").GetString();
                return $"Project deleted.\n  Name: {name}";
            }));
    }
}
