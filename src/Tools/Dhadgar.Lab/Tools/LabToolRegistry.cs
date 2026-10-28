namespace Dhadgar.Lab.Tools;

/// <summary>
/// Every <see cref="ILabTool"/> from DI, by name. Two tools with one name are a programming error and fail the daemon's start.
/// </summary>
public sealed class LabToolRegistry
{
    private readonly Dictionary<string, ILabTool> _tools = new(StringComparer.Ordinal);

    /// <summary>
    /// Builds the registry.
    /// </summary>
    /// <exception cref="InvalidOperationException">A tool has no name or two tools share one (a programming error).</exception>
    public LabToolRegistry(IEnumerable<ILabTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        foreach (ILabTool tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                throw new InvalidOperationException("A lab tool has no name.");
            }

            if (!_tools.TryAdd(tool.Name, tool))
            {
                throw new InvalidOperationException("Duplicate lab tool name: " + tool.Name);
            }
        }
    }

    /// <summary>
    /// Every registered tool.
    /// </summary>
    public IReadOnlyCollection<ILabTool> Tools => _tools.Values.ToArray();

    /// <summary>
    /// The tool with this name, or null.
    /// </summary>
    public ILabTool? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _tools.TryGetValue(name, out ILabTool? tool) ? tool : null;
    }
}
