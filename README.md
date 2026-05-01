# ProctorLti.DotNet

This folder is a **.NET 8** + **Angular 18** version of the D2L/Brightspace **LTI 1.3** proctor tool: an OIDC/LTI backend, a browser shell to open and control the Brightspace quiz tab, and an optional **Chrome/Edge** extension for reliable tab and overlay behavior.

| Project | Path | Role |
| --- | --- | --- |
| **API** | [ProctorLti.Api](ProctorLti.Api/) | Kestrel host: LTI login + launch, JWT validation, session handoff, static SPA when `wwwroot` is populated |
| **Web** | [ProctorLti.Web](ProctorLti.Web/) | Angular 18 app: proctor **shell** at `/shell?sid=…` |
| **Extension** | [extension](extension/) | Manifest V3 extension: quiz tab open/focus, pause overlay, close tab |

**Detailed docs:** [ProctorLti.Api/README.md](ProctorLti.Api/README.md) · [ProctorLti.Web/README.md](ProctorLti.Web/README.md) · [extension/README.md](extension/README.md)

## Prerequisites

- [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0) (or newer; SDK 9 can build `net8.0` projects)
- Node.js (LTS) and npm, for the Angular app
- Chrome or Edge, if you use the unpacked extension

## Configuration

1. Open **`ProctorLti.Api/appsettings.Development.json`** (or use environment variables / User Secrets) and set the **`LtiTool`** section: `PublicBaseUrl`, Brightspace `PlatformIssuer`, `PlatformOidcAuthUrl`, `PlatformJwksUri`, `LtiClientId`, `SessionSecret` (16+ characters), and optional `LtiTokenAudience`, `AllowedDeploymentIds`, `DefaultTestRunnerUrl`.
2. `PublicBaseUrl` must match the URL the LMS and learners use (including HTTPS and port when testing locally, e.g. `https://localhost:7237` with the default launch profile).

See [ProctorLti.Api/README.md#configuration](ProctorLti.Api/README.md#configuration) and the `LtiTool__*` environment variable table.

## Build the Angular app into the API

The API serves the SPA from **`ProctorLti.Api/wwwroot`** when you build the **`api`** configuration:

```bash
cd ProctorLti.Web
npm install
npx ng build --configuration api
```

## Run the integrated app

From the API project:

```bash
cd ProctorLti.Api
dotnet run --launch-profile https
```

Default URLs: **https://localhost:7237** and **http://localhost:5180** (`Properties/launchSettings.json`).

- **`GET /`** — Short HTML with LTI registration hints (login + redirect URLs).
- After a successful LTI **POST** to `/lti/launch`, the user is redirected to **`/shell?sid=…`**; the shell loads data from **`GET /api/session/{id}`**.

## Develop UI against the API (optional)

1. Start the API as above.
2. In another terminal:

   ```bash
   cd ProctorLti.Web
   npx ng serve
   ```

3. Open **http://localhost:4200**. The dev server proxies `/api`, `/lti`, and `/health` to the API (see `proxy.conf.json`). Real LTI **POST** flows still use the tool’s public **`PublicBaseUrl`**, not `localhost:4200`.

## Optional browser extension

Load the **`extension`** folder in **Load unpacked** (`chrome://extensions` or `edge://extensions`) so **Pause** (overlay) and **Stop** (close tab) can use `chrome.tabs` and scripting instead of plain `window.open` / `window.close` alone.

See [extension/README.md](extension/README.md) for permissions, security tightening, and troubleshooting.

## Solution file

`ProctorLti.sln` includes **ProctorLti.Api** only. The Angular app is a normal npm project under **`ProctorLti.Web`**.

## See also

- [ProctorLti.Api/README.md](ProctorLti.Api/README.md) — Endpoints and full configuration
- [ProctorLti.Web/README.md](ProctorLti.Web/README.md) — `ng serve`, proxy, build targets
- [extension/README.md](extension/README.md) — Install and behavior
