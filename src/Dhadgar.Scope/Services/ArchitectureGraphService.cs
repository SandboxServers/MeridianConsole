using System.Net.Http.Json;

namespace Dhadgar.Scope.Services;

public sealed class ArchitectureGraphService
{
    private readonly HttpClient _http;
    private ArchitectureGraphData? _cache;

    public ArchitectureGraphService(HttpClient http)
    {
        _http = http;
    }

    public async Task<ArchitectureGraphData> GetAsync()
    {
        if (_cache is not null) return _cache;

        var data = await _http.GetFromJsonAsync<ArchitectureGraphData>("content/architecture-park.v1.json");
        _cache = data ?? new ArchitectureGraphData();
        return _cache;
    }
}
