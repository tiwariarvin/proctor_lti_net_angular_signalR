# ProctorLti.Api

ASP.NET Core **8.0** backend for the D2L/Brightspace LTI 1.3 proctor tool. It implements OIDC login initiation, LTI launch validation (JWKS, nonce, claims), and a short-lived session handoff to the Angular shell.

## Endpoints

| Path | Method | Description |
| --- | --- | --- |
| `/health` | GET | Liveness: `{ "ok": true }` |
| `/lti/login` | GET, POST | OIDC third-party login initiation; redirects to the platform authorization URL |
| `/lti/launch` | POST | `id_token` + `state` (form post); validates token, stores launch context, **302** to `/shell?sid=…` |
| `/api/session/{id}` | GET | JSON for the shell: `testRunnerUrl`, `userName`, `deploymentId`, `controlChannel` (camelCase) |
| `/` | GET | Static HTML with tool registration hints (login + redirect URLs) |

When `wwwroot/` contains a built Angular app (`index.html`), static files and `MapFallbackToFile` serve the SPA. `Content-Security-Policy: frame-ancestors` is set for the shell to allow embedding from your LMS issuer.

## Configuration

Settings live under the **`LtiTool`** section in `appsettings.json` / `appsettings.{Environment}.json`, or as environment variables with the prefix **`LtiTool__`**.

| Key | Maps from Node `.env` (legacy) | Notes |
| --- | --- | --- |
| `PublicBaseUrl` | `PUBLIC_BASE_URL` | Public base URL of **this** tool (no trailing slash). Used for redirect URI, login URL, and post-launch redirect to `/shell` |
| `PlatformIssuer` | `PLATFORM_ISSUER` | Expected `iss` from the platform |
| `PlatformOidcAuthUrl` | `PLATFORM_OIDC_AUTH_URL` | Platform OIDC authorization endpoint |
| `PlatformJwksUri` | `PLATFORM_JWKS_URI` | JWKS document for validating `id_token` |
| `LtiClientId` | `LTI_CLIENT_ID` | LTI / OIDC client id |
| `LtiTokenAudience` | `LTI_TOKEN_AUDIENCE` | Optional; defaults to `LtiClientId` if empty |
| `AllowedDeploymentIds` | `ALLOWED_DEPLOYMENT_IDS` (comma-separated) | Empty = all deployments allowed |
| `DefaultTestRunnerUrl` | `DEFAULT_TEST_RUNNER_URL` | Optional default quiz URL if not in custom claims |
| `SessionSecret` | `SESSION_SECRET` | **Required**, at least 16 characters; signs OIDC `state` JWT (HS256) |

Example override:

`LtiTool__PublicBaseUrl=https://your-ngrok.example.com`

## Run

From this directory:

```bash
dotnet run --launch-profile https
```

Default dev profile listens on **https://localhost:7237** and **http://localhost:5180** (see `Properties/launchSettings.json`).

## Frontend

Build the Angular app into `wwwroot` from `../ProctorLti.Web`:

```bash
cd ../ProctorLti.Web
npx ng build --configuration api
```

Then run the API again; the shell is available at `{PublicBaseUrl}/shell?sid=…` after a successful launch.

## See also

- `../ProctorLti.Web/README.md` — Angular dev server and proxy
- `../extension/README.md` — optional browser extension for tab control
