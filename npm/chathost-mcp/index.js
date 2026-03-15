#!/usr/bin/env node
const { spawn } = require("child_process");
const path = require("path");

const PLATFORMS = {
  "linux-x64":    { pkg: "@chathost/mcp-linux-x64",    bin: "chathost-mcp" },
  "darwin-arm64": { pkg: "@chathost/mcp-darwin-arm64",  bin: "chathost-mcp" },
  "darwin-x64":   { pkg: "@chathost/mcp-darwin-x64",    bin: "chathost-mcp" },
  "win32-x64":    { pkg: "@chathost/mcp-win-x64",       bin: "chathost-mcp.exe" },
};

const key = `${process.platform}-${process.arch}`;
const platform = PLATFORMS[key];
if (!platform) {
  console.error(`Unsupported platform: ${key}`);
  process.exit(1);
}

let binPath;
try {
  binPath = path.join(require.resolve(`${platform.pkg}/package.json`), "..", "bin", platform.bin);
} catch {
  console.error(`Platform package ${platform.pkg} is not installed. Run: npm install ${platform.pkg}`);
  process.exit(1);
}

const child = spawn(binPath, process.argv.slice(2), {
  stdio: "inherit",
  env: process.env,
});
child.on("exit", (code) => process.exit(code ?? 1));
