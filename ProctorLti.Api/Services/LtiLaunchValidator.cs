using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ProctorLti.Api.Models;
using ProctorLti.Api.Options;

namespace ProctorLti.Api.Services;

public class LtiLaunchValidator(IOptions<LtiToolOptions> options, PlatformJwksProvider jwks)
{
    private const string DeploymentClaim = "https://purl.imsglobal.org/spec/lti/claim/deployment_id";
    private const string MessageTypeClaim = "https://purl.imsglobal.org/spec/lti/claim/message_type";
    private const string TargetLinkClaim = "https://purl.imsglobal.org/spec/lti/claim/target_link_uri";
    private const string ResourceLinkClaim = "https://purl.imsglobal.org/spec/lti/claim/resource_link";
    private const string ContextClaim = "https://purl.imsglobal.org/spec/lti/claim/context";
    private const string CustomClaim = "https://purl.imsglobal.org/spec/lti/claim/custom";

    private readonly LtiToolOptions _opt = options.Value;

    public async Task<LaunchBoot> VerifyAsync(string idToken, string nonce, CancellationToken ct = default)
    {
        var keys = await jwks.GetSigningKeysAsync(ct).ConfigureAwait(false);
        var audience = string.IsNullOrWhiteSpace(_opt.LtiTokenAudience)
            ? _opt.LtiClientId
            : _opt.LtiTokenAudience!;

        var issuer = _opt.PlatformIssuer.TrimEnd('/');
        var handler = new JsonWebTokenHandler();
        var validation = new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudiences = new[] { audience },
            IssuerSigningKeys = keys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            NameClaimType = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
        };

        var result = await handler.ValidateTokenAsync(idToken, validation).ConfigureAwait(false);
        if (!result.IsValid)
            throw new SecurityTokenException(result.Exception?.Message ?? "Invalid LTI id_token");

        var jwt = result.SecurityToken as JsonWebToken
                  ?? throw new InvalidOperationException("Expected JsonWebToken");

        var nonceClaim = jwt.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
        if (!string.Equals(nonceClaim, nonce, StringComparison.Ordinal))
            throw new SecurityTokenException("Invalid nonce");

        var deploymentId = jwt.Claims.FirstOrDefault(c => c.Type == DeploymentClaim)?.Value;
        if (string.IsNullOrEmpty(deploymentId))
            throw new SecurityTokenException("Missing deployment_id claim");

        if (_opt.AllowedDeploymentIds.Length > 0 && !_opt.AllowedDeploymentIds.Contains(deploymentId))
            throw new SecurityTokenException("Deployment not allowed");

        var messageType = jwt.Claims.FirstOrDefault(c => c.Type == MessageTypeClaim)?.Value;
        if (messageType != "LtiResourceLinkRequest")
            throw new SecurityTokenException($"Unsupported LTI message type: {messageType}");

        var targetLinkUri = jwt.Claims.FirstOrDefault(c => c.Type == TargetLinkClaim)?.Value;
        if (string.IsNullOrEmpty(targetLinkUri))
            throw new SecurityTokenException("Missing target_link_uri claim");

        var expected = new Uri(_opt.RedirectUri);
        var actual = new Uri(targetLinkUri);
        static string NormPath(string p) => (p.TrimEnd('/') == "" ? "/" : p.TrimEnd('/'));
        if (!string.Equals(actual.GetLeftPart(UriPartial.Authority), expected.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(NormPath(actual.AbsolutePath), NormPath(expected.AbsolutePath), StringComparison.Ordinal))
            throw new SecurityTokenException("target_link_uri does not match this tool launch URL");

        var read = new JwtSecurityTokenHandler().ReadJwtToken(idToken);
        var testRunnerUrl = ReadTestRunnerUrl(read, _opt.DefaultTestRunnerUrl);
        var name = read.Payload.Claims.FirstOrDefault(c => c.Type == "name")?.Value;
        var proctorRoomId = BuildProctorRoomId(deploymentId, read, targetLinkUri);

        return new LaunchBoot(testRunnerUrl, name, deploymentId, proctorRoomId);
    }

    private static string BuildProctorRoomId(string deploymentId, JwtSecurityToken read, string targetLinkUri)
    {
        var resourceLinkId = TryParseJsonClaimId(read, ResourceLinkClaim);
        if (!string.IsNullOrEmpty(resourceLinkId))
            return $"{deploymentId}|{resourceLinkId}";

        var contextId = TryParseJsonClaimId(read, ContextClaim);
        if (!string.IsNullOrEmpty(contextId))
            return $"{deploymentId}|ctx:{contextId}";

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(targetLinkUri), hash);
        var suffix = Convert.ToHexString(hash[..8]);
        return $"{deploymentId}|{suffix}";
    }

    private static string? TryParseJsonClaimId(JwtSecurityToken read, string claimType)
    {
        var raw = read.Payload.Claims.FirstOrDefault(c => c.Type == claimType)?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                return id.GetString();
        }
        catch (JsonException)
        {
            /* ignore */
        }

        return null;
    }

    private static string? ReadTestRunnerUrl(JwtSecurityToken token, string? defaultUrl)
    {
        var raw = token.Payload.Claims.FirstOrDefault(c => c.Type == CustomClaim)?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return defaultUrl;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return defaultUrl;
            if (root.TryGetProperty("test_runner_url", out var kebab) && kebab.ValueKind == JsonValueKind.String)
                return kebab.GetString();
            if (root.TryGetProperty("testRunnerUrl", out var camel) && camel.ValueKind == JsonValueKind.String)
                return camel.GetString();
        }
        catch (JsonException)
        {
            return defaultUrl;
        }

        return defaultUrl;
    }
}
