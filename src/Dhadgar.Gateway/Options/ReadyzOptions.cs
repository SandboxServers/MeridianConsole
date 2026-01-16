using System.Collections.ObjectModel;

namespace Dhadgar.Gateway.Options;

public sealed class ReadyzOptions
{
    public Collection<string> RequiredClusters { get; init; } = [];
    public int MinimumAvailableDestinations { get; init; } = 1;
    public bool FailOnMissingCluster { get; init; } = true;
}
