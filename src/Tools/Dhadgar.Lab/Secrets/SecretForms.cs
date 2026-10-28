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
/// on its way into a bundle or log file is still found. Temporary buffers that hold escaped text are cleared.
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

            return true;
        }
        finally
        {
            buffer.Clear();
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
