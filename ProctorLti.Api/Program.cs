using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using ProctorLti.Api.Models;
using ProctorLti.Api.Hubs;
using ProctorLti.Api.Options;
using ProctorLti.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddOptions<LtiToolOptions>()
    .BindConfiguration(LtiToolOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("platform-jwks");
builder.Services.AddSingleton<OidcStateService>();
builder.Services.AddSingleton<PlatformJwksProvider>();
builder.Services.AddSingleton<LtiLaunchValidator>();
builder.Services.AddSingleton<LaunchSessionStore>();

builder.Services.AddSignalR();

builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p => p
        .WithOrigins("http://localhost:4200", "https://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseCors();

var lti = app.Services.GetRequiredService<IOptions<LtiToolOptions>>().Value;

var wwwroot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var spaIndex = Path.Combine(wwwroot, "index.html");
var serveSpa = File.Exists(spaIndex);

if (serveSpa)
{
    var staticOptions = new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            var path = ctx.Context.Request.Path.Value ?? "";
            if (path.StartsWith("/shell", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/proctor", StringComparison.OrdinalIgnoreCase)
                || path == "/" || path == "")
            {
                var ancestors = $"'self' {lti.PlatformIssuer.TrimEnd('/')} {GetOrigin(lti.PlatformIssuer)}".Trim();
                ctx.Context.Response.Headers.ContentSecurityPolicy = $"frame-ancestors {ancestors}";
            }
        },
    };
    app.UseStaticFiles(staticOptions);
}

app.MapGet("/health", () => Results.Json(new { ok = true }));

app.MapGet("/api/session/{id}", (string id, LaunchSessionStore store) =>
{
    var boot = store.TryGet(id);
    return boot is null ? Results.NotFound() : Results.Json(boot);
});

app.MapHub<ProctorHub>("/hubs/proctor");

app.MapGet("/lti/login", HandleLogin);
app.MapPost("/lti/login", HandleLogin);

app.MapPost("/lti/launch", HandleLaunch);

app.MapGet("/", () =>
{
    var login = HtmlEncoder.Default.Encode(lti.LoginInitiationUri);
    var redirect = HtmlEncoder.Default.Encode(lti.RedirectUri);
    return Results.Content(
        $"<!doctype html><meta charset=\"utf-8\"><title>LTI tool</title>" +
        $"<p>OIDC Login initiation URL (register in Brightspace): <code>{login}</code></p>" +
        $"<p>Redirect / launch URL: <code>{redirect}</code></p>",
        "text/html");
});

if (serveSpa)
{
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            var path = ctx.Context.Request.Path.Value ?? "";
            if (!path.StartsWith("/shell", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("/proctor", StringComparison.OrdinalIgnoreCase)
                && path != "/" && path != "")
                return;

            var ancestors = $"'self' {lti.PlatformIssuer.TrimEnd('/')} {GetOrigin(lti.PlatformIssuer)}".Trim();
            ctx.Context.Response.Headers.ContentSecurityPolicy = $"frame-ancestors {ancestors}";
        },
    });
}

static string GetOrigin(string issuer)
{
    try
    {
        return new Uri(issuer).GetLeftPart(UriPartial.Authority);
    }
    catch
    {
        return "";
    }
}

static async Task<Dictionary<string, string>> MergeQueryAndForm(HttpRequest req)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var kv in req.Query)
    {
        if (kv.Value.Count > 0)
            d[kv.Key] = kv.Value.ToString();
    }

    if (req.HasFormContentType)
    {
        var form = await req.ReadFormAsync().ConfigureAwait(false);
        foreach (var kv in form)
        {
            if (kv.Value.Count > 0)
                d[kv.Key] = kv.Value.ToString();
        }
    }

    return d;
}

static string BuildAuthRedirectUrl(
    LtiToolOptions cfg,
    string clientId,
    string state,
    string nonce,
    string loginHint,
    string? ltiMessageHint,
    string targetLinkUri,
    string redirectUri)
{
    var baseAuth = new Uri(cfg.PlatformOidcAuthUrl);
    var ub = new UriBuilder(baseAuth);
    var existing = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
        ub.Query.Length > 0 ? ub.Query.TrimStart('?') : "");

    var coll = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    foreach (var kv in existing)
    {
        if (kv.Value.Count > 0)
            coll[kv.Key] = kv.Value.ToString();
    }

    void Set(string k, string v) => coll[k] = v;

    Set("response_type", "id_token");
    Set("response_mode", "form_post");
    Set("prompt", "none");
    Set("scope", "openid");
    Set("client_id", clientId);
    Set("redirect_uri", redirectUri);
    Set("state", state);
    Set("nonce", nonce);
    Set("login_hint", loginHint);
    if (!string.IsNullOrEmpty(ltiMessageHint))
        Set("lti_message_hint", ltiMessageHint);
    if (!string.IsNullOrEmpty(targetLinkUri))
        Set("target_link_uri", targetLinkUri);

    ub.Query = string.Join("&", coll.Select(kv =>
        $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}"));

    return ub.Uri.AbsoluteUri;
}

static string CreateNonce()
{
    var bytes = RandomNumberGenerator.GetBytes(24);
    return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

async Task<IResult> HandleLogin(
    HttpContext http,
    OidcStateService oidc,
    IOptions<LtiToolOptions> options)
{
    var cfg = options.Value;
    var q = await MergeQueryAndForm(http.Request).ConfigureAwait(false);

    q.TryGetValue("iss", out var iss);
    q.TryGetValue("login_hint", out var loginHint);
    q.TryGetValue("target_link_uri", out var targetLinkUri);
    q.TryGetValue("lti_message_hint", out var ltiMessageHint);
    q.TryGetValue("client_id", out var clientId);

    if (string.IsNullOrEmpty(iss) || string.IsNullOrEmpty(loginHint) ||
        string.IsNullOrEmpty(targetLinkUri) || string.IsNullOrEmpty(clientId))
        return Results.BadRequest("Missing OIDC parameters (iss, login_hint, target_link_uri, client_id).");

    if (!string.Equals(iss.TrimEnd('/'), cfg.PlatformIssuer.TrimEnd('/'), StringComparison.Ordinal))
        return Results.BadRequest("Unexpected issuer.");

    if (clientId != cfg.LtiClientId)
        return Results.BadRequest("Unexpected client_id.");

    var nonce = CreateNonce();
    string stateJwt;
    try
    {
        stateJwt = oidc.CreateStateJwt(
            new OidcStatePayload(iss, clientId, targetLinkUri, loginHint, ltiMessageHint),
            nonce);
    }
    catch
    {
        return Results.StatusCode(500);
    }

    var url = BuildAuthRedirectUrl(
        cfg,
        clientId,
        stateJwt,
        nonce,
        loginHint,
        string.IsNullOrEmpty(ltiMessageHint) ? null : ltiMessageHint,
        targetLinkUri,
        cfg.RedirectUri);

    return Results.Redirect(url);
}

async Task<IResult> HandleLaunch(
    HttpContext http,
    OidcStateService oidc,
    LtiLaunchValidator validator,
    LaunchSessionStore sessions,
    IOptions<LtiToolOptions> options)
{
    var cfg = options.Value;
    var form = await http.Request.ReadFormAsync().ConfigureAwait(false);
    var idToken = form["id_token"].ToString();
    var stateJwt = form["state"].ToString();

    if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(stateJwt))
        return Results.BadRequest("Missing id_token or state.");

    OidcStateVerified oidcCtx;
    try
    {
        oidcCtx = oidc.VerifyStateJwt(stateJwt);
    }
    catch
    {
        return Results.BadRequest("Invalid or expired OIDC state.");
    }

    if (!string.Equals(oidcCtx.Iss.TrimEnd('/'), cfg.PlatformIssuer.TrimEnd('/'), StringComparison.Ordinal))
        return Results.BadRequest("Unexpected issuer in OIDC state.");

    if (oidcCtx.ClientId != cfg.LtiClientId)
        return Results.BadRequest("Unexpected client_id in OIDC state.");

    LaunchBoot boot;
    try
    {
        boot = await validator.VerifyAsync(idToken, oidcCtx.Nonce, http.RequestAborted).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"LTI launch validation failed: {ex.Message}");
    }

    var sid = sessions.Put(boot);
    var shell = $"{cfg.PublicBaseUrl.TrimEnd('/')}/shell?sid={Uri.EscapeDataString(sid)}";
    return Results.Redirect(shell);
}

app.Run();
