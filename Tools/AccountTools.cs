using System.ComponentModel;
using System.Text.Json;
using ChatHost.Mcp.Services;
using ModelContextProtocol.Server;

namespace ChatHost.Mcp.Tools;

[McpServerToolType]
public class AccountTools
{
    [McpServerTool(
        Name = "account-whoami",
        Title = "Who Am I",
        ReadOnly = true,
        OpenWorld = false),
     Description("Get your chathost.io account information. Returns your account ID, email address, and account creation date. Use this to verify you are logged in and to check which account is active.")]
    public Task<string> WhoAmI(
        AuthService auth,
        IHttpClientFactory httpClientFactory)
    {
        return ToolHelpers.ExecuteTool(auth, httpClientFactory, "account-whoami",
            () => GetAccountInfo(auth, httpClientFactory, null));
    }

    [McpServerTool(
        Name = "account-login",
        Title = "Login",
        Idempotent = true,
        OpenWorld = false),
     Description("Sign in to your chathost.io account. If you are already signed in, returns your account info (ID, email, creation date). If not signed in, provides a URL to open in your browser to authenticate. You only need to sign in once — credentials are saved for future sessions.")]
    public Task<string> Login(
        AuthService auth,
        IHttpClientFactory httpClientFactory)
    {
        return ToolHelpers.ExecuteTool(auth, httpClientFactory, "account-login",
            () => GetAccountInfo(auth, httpClientFactory, "Already signed in!"));
    }

    [McpServerTool(
        Name = "account-logout",
        Title = "Logout",
        Destructive = true,
        OpenWorld = false),
     Description("Sign out of chathost.io by removing your saved credentials. After logging out, you will need to re-authenticate the next time you use any chathost tool. Use this if you want to switch accounts or revoke access.")]
    public string Logout(AuthService auth)
    {
        auth.ClearToken();
        return "Logged out of chathost.io. You will need to re-authenticate next time you use a chathost tool.";
    }

    private static Task<string> GetAccountInfo(
        AuthService auth,
        IHttpClientFactory httpClientFactory,
        string? prefix)
    {
        return ToolHelpers.CallApi(auth, httpClientFactory, "api/mcp/me", response =>
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            var email = root.GetProperty("email").GetString();
            var id = root.GetProperty("id").GetString();
            var createdAt = root.GetProperty("createdAt").GetString();

            var info = $"Account ID: {id}\nEmail: {email}\nCreated: {createdAt}";
            return prefix != null ? $"{prefix}\n\n{info}" : info;
        });
    }
}
