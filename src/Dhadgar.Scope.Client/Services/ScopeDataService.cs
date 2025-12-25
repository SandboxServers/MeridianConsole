using System.Net.Http.Json;
using Dhadgar.UI.Shared.Models;

namespace Dhadgar.Scope.Client.Services;

public sealed class ScopeDataService
{
    private readonly HttpClient _httpClient;

    public ScopeDataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IndexData> GetIndexAsync()
        => await _httpClient.GetFromJsonAsync<IndexData>("data/index.json") ?? new IndexData();

    public async Task<PageSectionsData> GetOverviewAsync()
        => await _httpClient.GetFromJsonAsync<PageSectionsData>("data/overview.json") ?? new PageSectionsData();

    public async Task<PageSectionsData> GetArchitectureAsync()
        => await _httpClient.GetFromJsonAsync<PageSectionsData>("data/architecture.json") ?? new PageSectionsData();

    public async Task<RoadmapData> GetRoadmapAsync()
        => await _httpClient.GetFromJsonAsync<RoadmapData>("data/roadmap.json") ?? new RoadmapData();

    public async Task<GovernanceData> GetGovernanceAsync()
        => await _httpClient.GetFromJsonAsync<GovernanceData>("data/governance.json") ?? new GovernanceData();
}
