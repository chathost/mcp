# chathost.io — Agentic First Web Hosting

> **Build a website with AI. Deploy it with one sentence.**

chathost.io is web hosting and internet infrastructure built for the age of AI agents. No git. No CLI. No hosting knowledge. Just tell your AI agent _"deploy my website"_ and get a live URL in minutes.

We believe the future of the internet is built by people who have never opened a terminal — powered by AI agents that handle the complexity for them.

---

## Get Started

### VS Code / Cursor — one click install

[![Install in VS Code](https://img.shields.io/badge/VS_Code-Install_Server-0098FF?style=for-the-badge&logo=visualstudiocode&logoColor=white)](https://insiders.vscode.dev/redirect/mcp/install?name=chathost&config=%7B%22command%22%3A%20%22npx%22%2C%20%22args%22%3A%20%5B%22-y%22%2C%20%22%40chathost/mcp%22%5D%7D)
[![Add to Cursor](https://img.shields.io/badge/Cursor-Add_Server-000000?style=for-the-badge&logo=cursor&logoColor=white)](https://cursor.com/en/install-mcp?name=chathost&config=eyJjb21tYW5kIjogIm5weCIsICJhcmdzIjogWyIteSIsICJAY2hhdGhvc3QvbWNwIl19)

### Claude Code

```bash
claude mcp add --transport stdio chathost -- npx -y @chathost/mcp
```

### Gemini CLI

```bash
gemini mcp add chathost npx -y @chathost/mcp
```

Then restart your agent and say **"deploy my website"**.

---

## How it works

1. Install the chathost MCP server in your AI agent (see above)
2. Ask your agent to create a website — or point it at one you've already built
3. Say _"deploy it to chathost"_
4. Your agent builds a Docker image, pushes it, and returns a live URL

That's it. Your site is on the internet.

---

## Early Adopters

We're in **early access** right now. We're looking for people who are building with AI agents and want the simplest path from idea to live website.

If you're using Claude Code, Cursor, Gemini, VS Code, or any MCP-compatible agent — we'd love to hear from you.

**Get in touch:** [contact@chathost.io](mailto:contact@chathost.io)

---

## Contributing

This repository is a read-only mirror of the primary repository. It is kept in sync automatically on each release.

Pull requests are welcome — once reviewed, accepted changes will be applied to the primary repo and mirrored back.

## License

MIT
