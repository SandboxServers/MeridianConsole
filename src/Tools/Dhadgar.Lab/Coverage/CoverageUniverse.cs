using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Coverage;

/// <summary>
/// One route of the coverage universe: an HTTP method (or <c>*</c>) and a path template such as
/// <c>/api/v1/agents/{nodeId}/heartbeat</c>. A <c>{**name}</c> segment is a catch-all.
/// </summary>
/// <param name="Method">The HTTP method in upper case, or <c>*</c> for any method.</param>
/// <param name="Template">The path template.</param>
public sealed record CoverageRoute(string Method, string Template);

/// <summary>
/// The coverage universe of one service: its routes, its error codes and its transitions (C18).
/// </summary>
/// <param name="Routes">The routes, in file order.</param>
/// <param name="ErrorCodes">The error codes the service can return.</param>
/// <param name="Transitions">The transitions in the form <c>entity:from-&gt;to</c>.</param>
public sealed record CoverageService(
    IReadOnlyList<CoverageRoute> Routes,
    IReadOnlyList<string> ErrorCodes,
    IReadOnlyList<string> Transitions);

/// <summary>
/// The shipped <c>coverage-universe.json</c> (L52). Loading validates the schema, the known services and entities, the
/// methods, the transition form and duplicate routes; any problem is a failure and nothing is thrown for it.
/// </summary>
public sealed class CoverageUniverse
{
    /// <summary>The file name, next to <c>Dhadgar.Lab.csproj</c> and copied to the output folder.</summary>
    public const string FileName = "coverage-universe.json";

    /// <summary>The only schema version this reader understands.</summary>
    public const int SupportedSchema = 1;

    /// <summary>The services a universe may name.</summary>
    public static readonly IReadOnlyList<string> KnownServices = ["nodes", "gateway"];

    /// <summary>The entities a transition may name (the snapshotter's tables).</summary>
    public static readonly IReadOnlyList<string> KnownEntities = ["node", "agent_certificate", "enrollment_token"];

    private static readonly HashSet<string> AllowedMethods = new(StringComparer.Ordinal)
    {
        "*", "GET", "POST", "PUT", "PATCH", "DELETE",
    };

    /// <summary>
    /// Creates a universe from parsed services.
    /// </summary>
    /// <param name="services">The services keyed by name.</param>
    public CoverageUniverse(IReadOnlyDictionary<string, CoverageService> services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>The services keyed by name.</summary>
    public IReadOnlyDictionary<string, CoverageService> Services { get; }

    /// <summary>
    /// Loads <c>coverage-universe.json</c> from <paramref name="labRoot"/>. The path is checked by
    /// <see cref="IPathValidator"/> against that folder before it is read.
    /// </summary>
    /// <param name="labRoot">The folder that holds the file (the lab's output folder).</param>
    /// <param name="validator">Validates the path.</param>
    public static Result<CoverageUniverse> Load(string labRoot, IPathValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        if (string.IsNullOrWhiteSpace(labRoot))
        {
            return Result<CoverageUniverse>.Failure("[Lab.InvalidArgument] the lab root is required");
        }

        Result<string> path = validator.ValidatePath(Path.Combine(labRoot, FileName), [labRoot]);
        if (path.IsFailure)
        {
            return Result<CoverageUniverse>.Failure("[Lab.InvalidArgument] the coverage universe path is outside the lab root");
        }

        if (!File.Exists(path.Value))
        {
            return Result<CoverageUniverse>.Failure("[Lab.Coverage.Missing] coverage-universe.json was not found");
        }

        string text;
        try
        {
            text = File.ReadAllText(path.Value);
        }
        catch (IOException)
        {
            return Result<CoverageUniverse>.Failure("[Lab.Coverage.Missing] coverage-universe.json could not be read");
        }

        return Parse(text);
    }

    /// <summary>
    /// Parses and validates the universe text. The text is never echoed in a failure.
    /// </summary>
    /// <param name="json">The JSON text of <c>coverage-universe.json</c>.</param>
    public static Result<CoverageUniverse> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Failure("the universe is empty");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Failure("the universe must be a JSON object");
            }

            if (!root.TryGetProperty("schema", out JsonElement schema) || schema.ValueKind != JsonValueKind.Number
                || !schema.TryGetInt32(out int schemaValue) || schemaValue != SupportedSchema)
            {
                return Failure("the universe schema must be " + SupportedSchema);
            }

            if (!root.TryGetProperty("services", out JsonElement servicesElement) || servicesElement.ValueKind != JsonValueKind.Object)
            {
                return Failure("the universe must hold a services object");
            }

            var services = new Dictionary<string, CoverageService>(StringComparer.Ordinal);
            foreach (JsonProperty property in servicesElement.EnumerateObject())
            {
                if (!KnownServices.Contains(property.Name, StringComparer.Ordinal))
                {
                    return Failure("unknown service " + property.Name);
                }

                Result<CoverageService> service = ParseService(property.Name, property.Value);
                if (service.IsFailure)
                {
                    return Result<CoverageUniverse>.Failure(service.Error);
                }

                services[property.Name] = service.Value;
            }

            return Result<CoverageUniverse>.Success(new CoverageUniverse(services));
        }
        catch (JsonException)
        {
            return Failure("the universe is not valid JSON");
        }
    }

    private static Result<CoverageService> ParseService(string name, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return ServiceFailure(name, "a service must be an object");
        }

        var routes = new List<CoverageRoute>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (element.TryGetProperty("routes", out JsonElement routesElement) && routesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement routeElement in routesElement.EnumerateArray())
            {
                if (!routeElement.TryGetProperty("method", out JsonElement method) || method.ValueKind != JsonValueKind.String
                    || !routeElement.TryGetProperty("template", out JsonElement template) || template.ValueKind != JsonValueKind.String)
                {
                    return ServiceFailure(name, "every route needs a method and a template");
                }

                string methodText = method.GetString() ?? string.Empty;
                string templateText = template.GetString() ?? string.Empty;
                if (!AllowedMethods.Contains(methodText))
                {
                    return ServiceFailure(name, "a route method is not allowed");
                }

                if (!templateText.StartsWith('/'))
                {
                    return ServiceFailure(name, "a route template must start with /");
                }

                if (!seen.Add(methodText + " " + templateText))
                {
                    return ServiceFailure(name, "duplicate route " + methodText + " " + templateText);
                }

                routes.Add(new CoverageRoute(methodText, templateText));
            }
        }
        else if (element.TryGetProperty("routes", out _))
        {
            return ServiceFailure(name, "routes must be an array");
        }

        Result<IReadOnlyList<string>> errorCodes = ParseStrings(name, element, "error_codes");
        if (errorCodes.IsFailure)
        {
            return Result<CoverageService>.Failure(errorCodes.Error);
        }

        Result<IReadOnlyList<string>> transitions = ParseStrings(name, element, "transitions");
        if (transitions.IsFailure)
        {
            return Result<CoverageService>.Failure(transitions.Error);
        }

        foreach (string transition in transitions.Value)
        {
            if (!IsKnownTransition(transition))
            {
                return ServiceFailure(name, "a transition must be entity:from->to with a known entity");
            }
        }

        return Result<CoverageService>.Success(new CoverageService(routes, errorCodes.Value, transitions.Value));
    }

    private static Result<IReadOnlyList<string>> ParseStrings(string name, JsonElement element, string property)
    {
        var values = new List<string>();
        if (!element.TryGetProperty(property, out JsonElement array))
        {
            return Result<IReadOnlyList<string>>.Success(values);
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return Result<IReadOnlyList<string>>.Failure(ServiceMessage(name, property + " must be an array"));
        }

        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                return Result<IReadOnlyList<string>>.Failure(ServiceMessage(name, property + " must hold non-empty strings"));
            }

            values.Add(item.GetString()!);
        }

        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            return Result<IReadOnlyList<string>>.Failure(ServiceMessage(name, property + " must not repeat a value"));
        }

        return Result<IReadOnlyList<string>>.Success(values);
    }

    /// <summary>
    /// True when <paramref name="transition"/> is <c>entity:from-&gt;to</c> with a known entity and non-empty states.
    /// </summary>
    /// <param name="transition">The transition text.</param>
    public static bool IsKnownTransition(string transition)
    {
        if (string.IsNullOrEmpty(transition))
        {
            return false;
        }

        int colon = transition.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return false;
        }

        int arrow = transition.IndexOf("->", colon + 1, StringComparison.Ordinal);
        if (arrow <= colon + 1 || arrow + 2 >= transition.Length)
        {
            return false;
        }

        return KnownEntities.Contains(transition[..colon], StringComparer.Ordinal);
    }

    private static Result<CoverageUniverse> Failure(string reason) =>
        Result<CoverageUniverse>.Failure("[Lab.Coverage.Invalid] " + reason);

    private static Result<CoverageService> ServiceFailure(string name, string reason) =>
        Result<CoverageService>.Failure(ServiceMessage(name, reason));

    private static string ServiceMessage(string name, string reason) =>
        "[Lab.Coverage.Invalid] service " + name + ": " + reason;
}
