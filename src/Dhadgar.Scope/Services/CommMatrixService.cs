using System.Net.Http.Json;

namespace Dhadgar.Scope.Services;

public sealed class CommMatrixService
{
    private readonly HttpClient _http;
    private CommMatrixData? _cache;

    public CommMatrixService(HttpClient http)
    {
        _http = http;
    }

    public async Task<CommMatrixData> GetAsync()
    {
        if (_cache is not null) return _cache;

        var data = await _http.GetFromJsonAsync<CommMatrixData>("content/comm-matrix.v1.json");
        _cache = data ?? new CommMatrixData();
        return _cache;
    }
}
