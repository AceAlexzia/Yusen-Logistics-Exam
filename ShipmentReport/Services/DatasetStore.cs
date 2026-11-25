using Microsoft.Extensions.Caching.Memory;

namespace ShipmentReport.Services;

/// <summary>
/// Keeps uploaded files in server memory for a few hours, keyed by a random id in the URL.
/// shortcut: memory only, so a restart drops uploads; move to a database if reports must be kept.
/// </summary>
public sealed class DatasetStore(IMemoryCache cache)
{
    static readonly MemoryCacheEntryOptions Options = new() { SlidingExpiration = TimeSpan.FromHours(4) };

    public string Add(Dataset ds)
    {
        var id = Guid.NewGuid().ToString("N");
        cache.Set("ds:" + id, ds, Options);
        return id;
    }

    public Dataset? Get(string? id) => id is null ? null : cache.Get<Dataset>("ds:" + id);
}
