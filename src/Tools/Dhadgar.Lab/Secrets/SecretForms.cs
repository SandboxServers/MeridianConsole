using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Dhadgar.Lab.Secrets;

/// <summary>
/// Visits one form of a secret. Returns false to stop the walk.
/// </summary>
/// <param name="form">The form to check. Valid only for the duration of the call.</param>
internal delegate bool SecretFormVisitor(ReadOnlySpan<char> form);

/// <summary>
/// The raw form of a vault value and the JSON-escaped forms a JSON writer can produce for it, so a secret that is escaped
/// on its way into a bundle or log file is still found, and, for PEM private keys, the single-line, body and body-line forms.
/// Temporary buffers that hold escaped text are cleared.
/// </summary>
internal static class SecretForms
{
    /// <summary>
    /// The encoders whose output can reach a bundle or log file. Add any encoder that <c>grep "Encoder ="</c> finds under src/Tools/Dhadgar.Lab.
    /// </summary>
    internal static readonly JavaScriptEncoder[] Encoders = [JavaScriptEncoder.Default, JavaScriptEncoder.UnsafeRelaxedJsonEscaping];

    /// <summary>
    /// Calls <paramref name="use"/> for the raw secret and then once for each JSON-escaped form (without quotes) that differs
    /// from it. Returns false as soon as <paramref name="use"/> returns false.
    /// </summary>
    /// <param name="secret">The raw vault value.</param>
    /// <param name="use">The visitor for each form.</param>
    internal static bool ForEachForm(ReadOnlySpan<char> secret, SecretFormVisitor use)
    {
        if (!use(secret))
        {
            return false;
        }

        var buffer = new ArrayBufferWriter<byte>();
        try
        {
            foreach (var encoder in Encoders)
            {
                if (!TryWriteEscaped(secret, encoder, buffer))
                {
                    continue;
                }

                var escaped = buffer.WrittenSpan[1..^1];
                var chars = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetCharCount(escaped));
                try
                {
                    var written = Encoding.UTF8.GetChars(escaped, chars);
                    var form = chars.AsSpan(0, written);
                    if (!form.SequenceEqual(secret) && !use(form))
                    {
                        return false;
                    }

                    // A writer may emit the same escape in lower case; the same form is checked again with that spelling.
                    if (LowercaseUnicodeEscapes(form) && !form.SequenceEqual(secret) && !use(form))
                    {
                        return false;
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(chars.AsSpan()));
                    ArrayPool<char>.Shared.Return(chars);
                    buffer.Clear();
                }
            }

            return ForEachPemForm(secret, use);
        }
        finally
        {
            buffer.Clear();
        }
    }

    private const int MinPemLine = 16;

    /// <summary>
    /// For a PEM private key, the forms a service can receive or print without the multi-line block: the key on one line
    /// (what <c>LabEnvValue.SingleLinePem</c> sends to BetterAuth), the base64 body alone, and each body line of at least
    /// 16 characters. The PEM regex in <see cref="Redactor"/> only matches when the header is present. The buffer is zeroed.
    /// </summary>
    private static bool ForEachPemForm(ReadOnlySpan<char> secret, SecretFormVisitor use)
    {
        if (!secret.StartsWith("-----BEGIN ", StringComparison.Ordinal) || secret.IndexOf("PRIVATE KEY-----", StringComparison.Ordinal) < 0)
        {
            return true;
        }

        char[] single = ArrayPool<char>.Shared.Rent(secret.Length);
        try
        {
            int length = 0;
            foreach (char c in secret)
            {
                if (c is not ('\r' or '\n'))
                {
                    single[length++] = c;
                }
            }

            ReadOnlySpan<char> line = single.AsSpan(0, length);
            if (!line.SequenceEqual(secret) && !use(line))
            {
                return false;
            }

            int headerEnd = line.IndexOf("KEY-----", StringComparison.Ordinal);
            int footer = line.IndexOf("-----END ", StringComparison.Ordinal);
            if (headerEnd >= 0 && footer > headerEnd + 8)
            {
                ReadOnlySpan<char> body = line[(headerEnd + 8)..footer];
                if (body.Length >= MinPemLine && !use(body))
                {
                    return false;
                }
            }

            foreach (Range range in secret.Split('\n'))
            {
                ReadOnlySpan<char> part = secret[range].TrimEnd('\r');
                if (part.Length >= MinPemLine && !part.StartsWith("-----", StringComparison.Ordinal) && !use(part))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(single.AsSpan()));
            ArrayPool<char>.Shared.Return(single);
        }
    }

    /// <summary>
    /// Lowercases the hex digits of every <c>\uXXXX</c> escape in <paramref name="text"/>, in place. An escaped backslash
    /// (<c>\\</c>) is skipped as a pair. Returns true when a digit changed.
    /// </summary>
    private static bool LowercaseUnicodeEscapes(Span<char> text)
    {
        bool changed = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length)
            {
                continue;
            }

            if (text[i + 1] == '\\')
            {
                i++;
                continue;
            }

            if (text[i + 1] != 'u' || i + 5 >= text.Length)
            {
                continue;
            }

            for (int digit = i + 2; digit <= i + 5; digit++)
            {
                if (text[digit] is >= 'A' and <= 'F')
                {
                    text[digit] = char.ToLowerInvariant(text[digit]);
                    changed = true;
                }
            }

            i += 5;
        }

        return changed;
    }

    /// <summary>
    /// Writes <paramref name="secret"/> as a JSON string with <paramref name="encoder"/>, including the quotes. Returns false
    /// when the text cannot be written as JSON (for example an unpaired surrogate); the raw form is still checked.
    /// </summary>
    private static bool TryWriteEscaped(ReadOnlySpan<char> secret, JavaScriptEncoder encoder, ArrayBufferWriter<byte> buffer)
    {
        try
        {
            using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = encoder });
            writer.WriteStringValue(secret);
            writer.Flush();
            return true;
        }
        catch (ArgumentException)
        {
            buffer.Clear();
            return false;
        }
    }
}
