using Microsoft.Extensions.Caching.Memory;
using ProctorLti.Api.Models;

namespace ProctorLti.Api.Services;

public class LaunchSessionStore(IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    public string Put(LaunchBoot boot)
    {
        var id = Guid.NewGuid().ToString("N");
        cache.Set(id, boot, Ttl);
        return id;
    }

    public LaunchBoot? TryGet(string id) =>
        cache.TryGetValue(id, out LaunchBoot? b) ? b : null;
}
