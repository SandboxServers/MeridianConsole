using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Contracts.Agents;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Windows;

/// <summary>
/// The agent state file as a redacted JSON document. The path is built from the state root and
/// <see cref="AgentWindowsFootprint"/>, then validated by <see cref="IPathValidator"/> before the read.
/// Properties whose names contain <c>token</c>, <c>key</c> or <c>secret</c> are blanked, then the whole document goes through
/// <see cref="Redactor"/>.
/// </summary>
public sealed class AgentStateReader
{
    /// <summary>
    /// The largest state file read, in bytes. A larger file is refused rather than loaded.
    /// </summary>
    public const long MaxStateBytes = 1024 * 1024;

    private static readonly string[] SensitiveWords = ["token", "key", "secret"];

    private readonly IPathValidator _validator;
    private readonly Redactor _redactor;
    private readonly string _programDataRoot;

    /// <summary>
    /// Creates the reader over the machine's <c>%ProgramData%</c>.
    /// </summary>
    public AgentStateReader(IPathValidator validator, Redactor redactor)
        : this(validator, redactor, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
    {
    }

    /// <summary>
    /// Creates the reader over <paramref name="programDataRoot"/>; used by tests with a temporary root.
    /// </summary>
    internal AgentStateReader(IPathValidator validator, Redactor redactor, string programDataRoot)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _programDataRoot = programDataRoot ?? throw new ArgumentNullException(nameof(programDataRoot));
    }

    /// <summary>
    /// Reads the state file. A missing file is <c>Exists = false</c>, not a failure.
    /// </summary>
    public Result<AgentStateSnapshot> Read()
    {
        var stateDirectory = Path.Combine(_programDataRoot, AgentWindowsFootprint.StateDirectoryRelative);
        var candidate = Path.Combine(stateDirectory, AgentWindowsFootprint.StateFileName);

        var validated = _validator.ValidatePath(candidate, [stateDirectory]);
        if (validated.IsFailure)
        {
            return Result<AgentStateSnapshot>.Failure("[Lab.InvalidArgument] the state file path is not allowed");
        }

        var path = validated.Value!;
        if (!File.Exists(path))
        {
            return Result<AgentStateSnapshot>.Success(new AgentStateSnapshot(false, path, null));
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxStateBytes)
            {
                return Result<AgentStateSnapshot>.Failure("[Lab.Unavailable] the state file is larger than 1 MiB");
            }

            var text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text);
            using var blanked = JsonDocument.Parse(Blank(document.RootElement));
            JsonElement redacted = _redactor.Redact(blanked.RootElement);
            return Result<AgentStateSnapshot>.Success(new AgentStateSnapshot(true, path, redacted));
        }
        catch (JsonException)
        {
            // The message is not returned: a parse error can echo the file's content.
            return Result<AgentStateSnapshot>.Failure("[Lab.Unavailable] the state file is not valid JSON");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<AgentStateSnapshot>.Failure("[Lab.Unavailable] the state file could not be read");
        }
    }

    /// <summary>
    /// Writes <paramref name="element"/> with every sensitive property's scalar value replaced by <c>null</c>.
    /// </summary>
    private static byte[] Blank(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteBlanked(writer, element, null);
        }

        return stream.ToArray();
    }

    private static void WriteBlanked(Utf8JsonWriter writer, JsonElement element, string? propertyName)
    {
        var isContainer = element.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        if (propertyName is not null && !isContainer && IsSensitive(propertyName))
        {
            writer.WriteNullValue();
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteBlanked(writer, property.Value, property.Name);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteBlanked(writer, item, null);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitive(string name)
    {
        foreach (var word in SensitiveWords)
        {
            if (name.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The state file's presence, its validated path and its redacted content (null when the file is absent).
/// </summary>
/// <param name="Exists">Whether the file exists.</param>
/// <param name="Path">The validated full path.</param>
/// <param name="Content">The blanked and redacted JSON, or null.</param>
public sealed record AgentStateSnapshot(bool Exists, string Path, JsonElement? Content);
