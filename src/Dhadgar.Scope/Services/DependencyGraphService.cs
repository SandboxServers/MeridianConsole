using System.Net.Http.Json;

namespace Dhadgar.Scope.Services;

public sealed class DependencyGraphService
{
    private readonly HttpClient _http;
    private DependencyGraphData? _cache;

    public DependencyGraphService(HttpClient http)
    {
        _http = http;
    }

    public async Task<DependencyGraphData> GetAsync()
    {
        if (_cache is not null) return _cache;

        var data = await _http.GetFromJsonAsync<DependencyGraphData>("content/dependencies.json");
        _cache = data ?? new DependencyGraphData();
        return _cache;
    }
}
