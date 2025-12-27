using System.Net.Http.Json;

namespace Dhadgar.Scope.Services;

public sealed class DbSchemaCatalogService
{
    private readonly HttpClient _http;
    private DbSchemaCatalog? _cache;

    public DbSchemaCatalogService(HttpClient http)
    {
        _http = http;
    }

    public async Task<DbSchemaCatalog> GetAsync()
    {
        if (_cache is not null) return _cache;

        var data = await _http.GetFromJsonAsync<DbSchemaCatalog>("content/db-schemas.v1.json");
        _cache = data ?? new DbSchemaCatalog();
        return _cache;
    }
}
