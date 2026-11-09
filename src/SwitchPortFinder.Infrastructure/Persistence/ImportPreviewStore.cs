using Microsoft.Extensions.Caching.Memory;
using SwitchPortFinder.Application.Import;

namespace SwitchPortFinder.Infrastructure.Persistence;

/// <summary>Holds previewed imports in memory only (they may contain secrets) with a short TTL.</summary>
public class ImportPreviewStore(IMemoryCache cache) : IImportPreviewStore
{
    public string Put(StoredImport import, TimeSpan ttl)
    {
        var id = Guid.NewGuid().ToString("N");
        cache.Set("import:" + id, import, ttl);
        return id;
    }

    public StoredImport? Take(string id)
    {
        if (!cache.TryGetValue("import:" + id, out StoredImport? import)) return null;
        cache.Remove("import:" + id);
        return import;
    }
}
