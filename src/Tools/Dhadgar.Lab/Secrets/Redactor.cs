using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Dhadgar.Lab.Secrets;

/// <summary>
/// Removes secrets from text and JSON before they reach a log, an audit line or a tool result. Vault values of at least
/// eight characters are replaced longest first with <c>***&lt;ref&gt;</c>; then PEM private key blocks, bearer tokens and
/// <c>Password=</c>/<c>Pwd=</c> segments are masked. Pattern matching has a one second timeout; a timeout masks the whole text.
/// </summary>
public sealed class Redactor
{
    private const int MinSecretLength = 8;
    private const string FailClosedText = "***redaction-timeout***";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex PemBlock = new(
        @"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z ]*PRIVATE KEY-----|\z)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex BearerToken = new(
        @"Bearer\s+[A-Za-z0-9._~+/=-]{20,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout);

    private static readonly Regex PasswordSegment = new(
        @"(Password|Pwd)=[^;]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout);

    private readonly SecretVault _vault;

    /// <summary>
    /// Creates a redactor over <paramref name="vault"/>. The vault is not owned by the redactor.
    /// </summary>
    public Redactor(SecretVault vault)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    /// <summary>
    /// Returns <paramref name="text"/> with every secret masked.
    /// </summary>
    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            var redacted = text;

            // Longest first, so a secret that contains a shorter one is masked whole.
            foreach (var (reference, length) in _vault.Snapshot().OrderByDescending(entry => entry.Length))
            {
                if (length < MinSecretLength)
                {
                    continue;
                }

                var replacement = "***" + reference;
                var masked = _vault.Use(reference, secret => ReplaceAll(redacted, secret, replacement));
                if (masked.IsSuccess)
                {
                    redacted = masked.Value;
                }
            }

            redacted = PemBlock.Replace(redacted, "***pem");
            redacted = BearerToken.Replace(redacted, "Bearer ***");
            redacted = PasswordSegment.Replace(redacted, "$1=***");
            return redacted;
        }
        catch (RegexMatchTimeoutException)
        {
            return FailClosedText;
        }
    }

    /// <summary>
    /// Returns a copy of <paramref name="json"/> with every string value redacted. Numbers, booleans and nulls are kept.
    /// </summary>
    public JsonElement Redact(JsonElement json)
    {
        var node = ToNode(json);
        return JsonSerializer.SerializeToElement<JsonNode?>(node);
    }

    private JsonNode? ToNode(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return JsonValue.Create(Redact(element.GetString() ?? string.Empty));

            case JsonValueKind.Object:
                var jsonObject = new JsonObject();
                foreach (var property in element.EnumerateObject())
                {
                    jsonObject[Redact(property.Name)] = ToNode(property.Value);
                }

                return jsonObject;

            case JsonValueKind.Array:
                var jsonArray = new JsonArray();
                foreach (var item in element.EnumerateArray())
                {
                    jsonArray.Add(ToNode(item));
                }

                return jsonArray;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return JsonNode.Parse(element.GetRawText());

            default:
                return null;
        }
    }

    private static string ReplaceAll(string text, ReadOnlySpan<char> secret, string replacement)
    {
        if (secret.IsEmpty)
        {
            return text;
        }

        var source = text.AsSpan();
        var builder = new StringBuilder(text.Length);
        var position = 0;
        while (true)
        {
            var relative = source.Slice(position).IndexOf(secret, StringComparison.Ordinal);
            if (relative < 0)
            {
                break;
            }

            builder.Append(source.Slice(position, relative)).Append(replacement);
            position += relative + secret.Length;
        }

        builder.Append(source.Slice(position));
        return builder.ToString();
    }
}
