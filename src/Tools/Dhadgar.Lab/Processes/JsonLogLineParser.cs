using System.Globalization;
using System.Text.Json;

namespace Dhadgar.Lab.Processes;

/// <summary>
/// Parses one captured line. A console-formatter JSON line (<c>Logging__Console__FormatterName=json</c>) yields its
/// timestamp, level, category, event id and message. Anything else is kept as the message with level <c>Raw</c>.
/// </summary>
public static class JsonLogLineParser
{
    /// <summary>
    /// The level given to a line that is not a console JSON log entry.
    /// </summary>
    public const string RawLevel = "Raw";

    /// <summary>
    /// Parses a line. Never throws for malformed input.
    /// </summary>
    /// <param name="line">The redacted line.</param>
    /// <param name="fallbackTimestamp">The timestamp used when the line carries none.</param>
    public static LogLine Parse(string line, DateTimeOffset fallbackTimestamp)
    {
        ArgumentNullException.ThrowIfNull(line);
        LogLine? parsed = TryParseJson(line, fallbackTimestamp);
        return parsed ?? new LogLine(fallbackTimestamp, RawLevel, string.Empty, null, line);
    }

    private static LogLine? TryParseJson(string line, DateTimeOffset fallback)
    {
        string trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            JsonElement? message = Find(root, "Message");
            if (message is not { ValueKind: JsonValueKind.String } messageValue)
            {
                return null;
            }

            DateTimeOffset timestamp = fallback;
            if (Find(root, "Timestamp") is { ValueKind: JsonValueKind.String } stamp
                && DateTimeOffset.TryParse(stamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
            {
                timestamp = parsed;
            }

            string level = Find(root, "LogLevel") is { ValueKind: JsonValueKind.String } levelValue
                ? levelValue.GetString() ?? RawLevel
                : RawLevel;
            string category = Find(root, "Category") is { ValueKind: JsonValueKind.String } categoryValue
                ? categoryValue.GetString() ?? string.Empty
                : string.Empty;

            return new LogLine(timestamp, level, category, ReadEventId(Find(root, "EventId")), messageValue.GetString() ?? string.Empty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadEventId(JsonElement? eventId)
    {
        if (eventId is not { } value)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int direct))
        {
            return direct;
        }

        if (value.ValueKind == JsonValueKind.Object
            && Find(value, "Id") is { ValueKind: JsonValueKind.Number } id
            && id.TryGetInt32(out int nested))
        {
            return nested;
        }

        return null;
    }

    private static JsonElement? Find(JsonElement obj, string name)
    {
        foreach (JsonProperty property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}
