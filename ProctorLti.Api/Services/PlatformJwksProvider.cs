using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProctorLti.Api.Options;

namespace ProctorLti.Api.Services;

public class PlatformJwksProvider(IHttpClientFactory httpFactory, IMemoryCache cache, IOptions<LtiToolOptions> options)
{
    public async Task<IList<SecurityKey>> GetSigningKeysAsync(CancellationToken ct = default)
    {
        var uri = options.Value.PlatformJwksUri;
        var keys = await cache.GetOrCreateAsync(
            $"jwks:{uri}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
                var client = httpFactory.CreateClient("platform-jwks");
                var json = await client.GetStringAsync(new Uri(uri), ct);
                var jwks = new JsonWebKeySet(json);
                return jwks.GetSigningKeys();
            }).ConfigureAwait(false);

        return keys ?? (IList<SecurityKey>)Array.Empty<SecurityKey>();
    }
}
