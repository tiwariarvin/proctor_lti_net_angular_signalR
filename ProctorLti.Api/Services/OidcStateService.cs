using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProctorLti.Api.Options;

namespace ProctorLti.Api.Services;

public record OidcStatePayload(
    string PlatformIss,
    string ClientId,
    string TargetLinkUri,
    string LoginHint,
    string? LtiMessageHint);

public record OidcStateVerified(
    string Nonce,
    string Iss,
    string ClientId,
    string TargetLinkUri,
    string LoginHint,
    string LtiMessageHint);

public class OidcStateService(IOptions<LtiToolOptions> options)
{
    private readonly SymmetricSecurityKey _key = new(Encoding.UTF8.GetBytes(options.Value.SessionSecret));

    public string CreateStateJwt(OidcStatePayload payload, string nonce)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, nonce),
            new("platformIss", payload.PlatformIss),
            new("clientId", payload.ClientId),
            new("targetLinkUri", payload.TargetLinkUri),
            new("loginHint", payload.LoginHint),
            new("ltiMessageHint", payload.LtiMessageHint ?? ""),
        };

        var desc = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = "d2l-lti-test-runner-wrapper",
            Audience = "lti-oidc-state",
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(desc);
        return handler.WriteToken(token);
    }

    public OidcStateVerified VerifyStateJwt(string jwt)
    {
        var handler = new JwtSecurityTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _key,
            ValidIssuer = "d2l-lti-test-runner-wrapper",
            ValidAudience = "lti-oidc-state",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        var principal = handler.ValidateToken(jwt, parameters, out _);
        var nonce = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? "";
        var iss = principal.FindFirst("platformIss")?.Value ?? "";
        var clientId = principal.FindFirst("clientId")?.Value ?? "";
        var targetLinkUri = principal.FindFirst("targetLinkUri")?.Value ?? "";
        var loginHint = principal.FindFirst("loginHint")?.Value ?? "";
        var ltiMessageHint = principal.FindFirst("ltiMessageHint")?.Value ?? "";

        if (string.IsNullOrEmpty(nonce))
            throw new SecurityTokenException("Invalid OIDC state: missing nonce");
        if (string.IsNullOrEmpty(iss) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(targetLinkUri))
            throw new SecurityTokenException("Invalid OIDC state: missing fields");

        return new OidcStateVerified(nonce, iss, clientId, targetLinkUri, loginHint, ltiMessageHint);
    }
}
