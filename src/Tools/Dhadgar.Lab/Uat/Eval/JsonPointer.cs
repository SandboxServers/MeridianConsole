using System.Globalization;
using System.Text.Json;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// The outcome of a path lookup that parsed: <see cref="Found"/> is false when the path names nothing.
/// </summary>
public readonly record struct PointerLookup(bool Found, JsonElement Value);

/// <summary>
/// RFC 6901 JSON Pointer over <see cref="JsonElement"/> (C7 <c>path</c>, <c>~0</c> and <c>~1</c> escapes).
/// An empty path names the whole document. A malformed path is a failure; a well-formed path that names
/// nothing is a success with <see cref="PointerLookup.Found"/> false, so callers can tell "missing" from "broken".
/// </summary>
public static class JsonPointer
{
    /// <summary>Resolves <paramref name="path"/> against <paramref name="root"/>.</summary>
    public static Result<PointerLookup> Resolve(JsonElement root, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length == 0)
        {
            return Result<PointerLookup>.Success(new PointerLookup(true, root));
        }

        if (path[0] != '/')
        {
            return Result<PointerLookup>.Failure("[Lab.InvalidArgument] path must be empty or start with /");
        }

        JsonElement current = root;
        foreach (string raw in path[1..].Split('/'))
        {
            if (!IsWellEscaped(raw))
            {
                return Result<PointerLookup>.Failure("[Lab.InvalidArgument] path has a bad ~ escape");
            }

            string token = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(token, out JsonElement next))
                {
                    return NotFound();
                }

                current = next;
            }
            else if (current.ValueKind == JsonValueKind.Array && TryIndex(token, current.GetArrayLength(), out int index))
            {
                current = current[index];
            }
            else
            {
                return NotFound();
            }
        }

        return Result<PointerLookup>.Success(new PointerLookup(true, current));
    }

    private static Result<PointerLookup> NotFound() =>
        Result<PointerLookup>.Success(new PointerLookup(false, default));

    private static bool IsWellEscaped(string token)
    {
        for (int i = 0; i < token.Length; i++)
        {
            if (token[i] != '~')
            {
                continue;
            }

            if (i + 1 >= token.Length || (token[i + 1] != '0' && token[i + 1] != '1'))
            {
                return false;
            }

            i++;
        }

        return true;
    }

    private static bool TryIndex(string token, int length, out int index)
    {
        index = -1;
        if (token.Length == 0 || token.Any(c => c < '0' || c > '9') || (token.Length > 1 && token[0] == '0'))
        {
            return false;
        }

        return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index < length;
    }
}
