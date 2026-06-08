Unity v6000.3.7f1

## Synaptic AI Pro — MCP Server setup

This project uses the **Synaptic AI Pro** asset, whose MCP server runs on Node.js.
Its `Assets/Synaptic AI Pro/MCPServer/node_modules/` folder is intentionally
**git-ignored** — npm installs 13,000+ dependency files that are heavyweight to
commit and aren't involved in the Unity build, so we restore them locally instead
of tracking them in the repo.

**After cloning (or whenever the MCP server fails to start), run once:**

```
install-synaptic-mcp.bat
```
Or you can simply run `npm install` in the `Assets/Synaptic AI Pro/MCPServer` directory.

Requires [Node.js](https://nodejs.org/)