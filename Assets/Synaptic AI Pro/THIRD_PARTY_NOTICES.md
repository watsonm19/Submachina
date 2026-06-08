# Third-Party Notices

Synaptic AI Pro for Unity uses third-party components.
This document lists those components and their respective licenses.

---

## Bundled Assets

### Kenney Particle Pack
- **Source**: https://kenney.nl/
- **License**: Creative Commons CC0 1.0 Universal (Public Domain Dedication)
- **License URL**: https://creativecommons.org/publicdomain/zero/1.0/
- **Files**: `Resources/VFX/Textures/*.png`
- **License Text**: `Resources/VFX/Textures/Kenney_License.txt`
- **Notice**:
  > Particle Pack (1.1) by Kenney Vleugels (Kenney.nl)
  > You may use these assets in personal and commercial projects.
  > Credit (Kenney or www.kenney.nl) would be nice but is not mandatory.

---

## Node.js Dependencies (MCPServer/node_modules)

The MCPServer component is built on Node.js and uses several npm packages.
Each package retains its own license, included within its respective folder
under `MCPServer/node_modules/`.

### Major Dependencies

| Package | License | Purpose |
|---|---|---|
| `@modelcontextprotocol/sdk` | MIT | MCP protocol implementation |
| `ws` | MIT | WebSocket client/server |
| `zod` | MIT | Schema validation |
| `zod-to-json-schema` | ISC | Schema conversion |
| `openai` | Apache-2.0 | OpenAI API client |
| `router` | MIT | HTTP routing |
| `debug` | MIT | Debug logging |
| `mime-db` / `mime-types` | MIT | MIME type detection |
| `combined-stream` | MIT | Stream utilities |
| `web-streams-polyfill` | MIT | Streams polyfill |
| `undici-types` | MIT | HTTP client types |
| `cookie-signature` | MIT | Cookie signing |

For the complete list and individual license texts, see the `LICENSE` file
within each subdirectory of `MCPServer/node_modules/`.

---

## Unity Standard Components

This product uses standard Unity Engine APIs and packages, including:
- Unity Engine (Unity Technologies)
- Newtonsoft.Json for Unity (auto-installed by Unity Package Manager)
- Cinemachine (Unity package)
- TextMeshPro (Unity package)
- VFX Graph (Unity package, optional)
- Universal Render Pipeline (Unity package, optional)
- High Definition Render Pipeline (Unity package, optional)

These components are governed by Unity's standard licensing terms and
are not redistributed by Synaptic AI Pro.

---

## Synaptic Original Components

All shaders under `Shaders/` (Sky, Water, Grass, Character, Caustics, Toon,
SkySphere, Effects) and all C# scripts under `Editor/` and `Runtime/`
are original works authored by mizumaru (Synaptic), and are licensed
under the terms in `LICENSE.md`.

---

## Reporting Issues

If you believe any component listed here is incorrectly attributed, or if
you notice an unattributed third-party component, please contact:

- Email: sekiguchimiu@gmail.com
- Discord: https://discord.gg/Y2nUyWvqR3

We take licensing seriously and will address any concerns promptly.

---

Last updated: 2026-05-12
