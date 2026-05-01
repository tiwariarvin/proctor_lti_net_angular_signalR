# ProctorLti.Web

**Angular 18** standalone application for the LTI proctor **shell** (`/shell`). After the API validates an LTI launch, the browser is redirected to `/shell?sid={id}`; the app calls `GET /api/session/{id}` to load `testRunnerUrl` and user metadata, then replicates the “Open quiz / Play / Pause / Stop” flow.

## Prerequisites

- Node.js (LTS) and npm
- The API (`../ProctorLti.Api`) when using the dev proxy

## Development server

```bash
npm install
npx ng serve
```

By default the app is at `http://localhost:4200/`. The dev server uses **`proxy.conf.json`** to forward `/api`, `/lti`, and `/health` to `https://localhost:7237` (run the API with the **https** launch profile so ports match). Adjust `proxy.conf.json` if your API uses different URLs or HTTP-only.

**Brightspace and real LTI posts** must target the tool’s public URL (the API), not `localhost:4200`. The proxy is for local UI work against a running API.

## Build (ship into the API)

Output is configured for the **`api`** build: files go to `../ProctorLti.Api/wwwroot` so Kestrel can host the SPA and the API on one origin.

```bash
npx ng build --configuration api
```

For a normal local `dist` build without copying to the API:

```bash
npx ng build
```

## Code layout

- `src/app/shell/` — Proctor UI (quiz tab controls)
- `src/app/services/session.service.ts` — Fetches `LaunchBoot` from `/api/session/{id}`
- `src/app/app.routes.ts` — `shell` route; default redirect to `shell` for the dev app root

## Testing the shell only

1. Start the API (`../ProctorLti.Api`) with a valid `LtiTool` configuration.
2. `npx ng build --configuration api` (or use `ng serve` with proxy) so `/api/session/...` resolves.
3. Complete a real LTI launch (or manually create a session only if you add a dev aid—there is no mock session in production code).

## Further help

[Angular CLI documentation](https://angular.dev/tools/cli) — `ng help`, schematics, and more.

## See also

- `../ProctorLti.Api/README.md` — LTI settings and endpoints
- `../extension/README.md` — optional extension for tab overlay and `chrome.tabs` control
