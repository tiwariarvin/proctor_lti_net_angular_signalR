using System.ComponentModel.DataAnnotations;

namespace ProctorLti.Api.Options;

public class LtiToolOptions
{
    public const string SectionName = "LtiTool";

    [Required]
    public string PublicBaseUrl { get; set; } = "";

    [Required]
    public string PlatformIssuer { get; set; } = "";

    [Required]
    public string PlatformOidcAuthUrl { get; set; } = "";

    [Required]
    public string PlatformJwksUri { get; set; } = "";

    [Required]
    public string LtiClientId { get; set; } = "";

    public string? LtiTokenAudience { get; set; }

    public string[] AllowedDeploymentIds { get; set; } = [];

    public string? DefaultTestRunnerUrl { get; set; }

    [Required]
    [MinLength(16)]
    public string SessionSecret { get; set; } = "";

    public string RedirectUri => $"{PublicBaseUrl.TrimEnd('/')}/lti/launch";

    public string LoginInitiationUri => $"{PublicBaseUrl.TrimEnd('/')}/lti/login";
}
