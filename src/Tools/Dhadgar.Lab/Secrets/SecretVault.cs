using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Dhadgar.Shared.Results;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Dhadgar.Lab.Tests")]

namespace Dhadgar.Lab.Secrets;

/// <summary>
/// Kinds of secret the vault holds. Each kind has its own reference prefix (tok-, key-, pwd-, brr-).
/// </summary>
public enum SecretKind { EnrollmentToken, PrivateKey, Password, BearerToken }

/// <summary>
/// Holds secrets as <c>char[]</c> and hands them out only by reference. The value is never copied out:
/// <see cref="Use{T}"/> passes a span over the stored array. <see cref="Remove"/> and <see cref="Dispose"/> zero the storage.
/// The delegate given to <see cref="Use{T}"/> runs under the vault lock and must not call back into the vault.
/// </summary>
public sealed class SecretVault : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, char[]> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<SecretKind, int> _counters = [];
    private bool _disposed;

    /// <summary>
    /// Stores a copy of <paramref name="value"/> and returns its reference ("tok-1", "key-1", "pwd-1", "brr-1").
    /// The caller still owns its own buffer and must zero it. Throws <see cref="ObjectDisposedException"/> after <see cref="Dispose"/>.
    /// </summary>
    public string Add(SecretKind kind, ReadOnlySpan<char> value)
    {
        var prefix = PrefixFor(kind);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var next = _counters.GetValueOrDefault(kind) + 1;
            _counters[kind] = next;
            var reference = $"{prefix}-{next}";
            _values[reference] = value.ToArray();
            return reference;
        }
    }

    /// <summary>
    /// Runs <paramref name="use"/> over the stored value without copying it out.
    /// Returns <c>[Lab.NotFound]</c> when the reference is not in the vault.
    /// </summary>
    public Result<T> Use<T>(string secretRef, Func<ReadOnlySpan<char>, T> use)
    {
        ArgumentNullException.ThrowIfNull(secretRef);
        ArgumentNullException.ThrowIfNull(use);

        lock (_gate)
        {
            if (_disposed || !_values.TryGetValue(secretRef, out var stored))
            {
                return Result<T>.Failure("[Lab.NotFound] secret reference is not in the vault");
            }

            return Result<T>.Success(use(stored));
        }
    }

    /// <summary>
    /// True when the reference is currently stored.
    /// </summary>
    public bool Contains(string secretRef)
    {
        ArgumentNullException.ThrowIfNull(secretRef);

        lock (_gate)
        {
            return !_disposed && _values.ContainsKey(secretRef);
        }
    }

    /// <summary>
    /// Removes a value and zeroes its storage. Unknown references are a no-op.
    /// </summary>
    public void Remove(string secretRef)
    {
        ArgumentNullException.ThrowIfNull(secretRef);

        lock (_gate)
        {
            if (_values.Remove(secretRef, out var stored))
            {
                Zero(stored);
            }
        }
    }

    /// <summary>
    /// Zeroes every stored value and closes the vault. Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var stored in _values.Values)
            {
                Zero(stored);
            }

            _values.Clear();
            _disposed = true;
        }
    }

    /// <summary>
    /// References and lengths of every stored value, for the redactor. Never the values.
    /// </summary>
    internal IReadOnlyList<(string Reference, int Length)> Snapshot()
    {
        lock (_gate)
        {
            return _values.Select(entry => (entry.Key, entry.Value.Length)).ToList();
        }
    }

    /// <summary>
    /// Test hook: the live storage array for <paramref name="secretRef"/>, or null. Used to prove that
    /// <see cref="Remove"/> and <see cref="Dispose"/> zero the bytes the vault held.
    /// </summary>
    internal char[]? PeekForTests(string secretRef)
    {
        lock (_gate)
        {
            return _values.TryGetValue(secretRef, out var stored) ? stored : null;
        }
    }

    private static void Zero(char[] value)
    {
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(value.AsSpan()));
    }

    private static string PrefixFor(SecretKind kind) => kind switch
    {
        SecretKind.EnrollmentToken => "tok",
        SecretKind.PrivateKey => "key",
        SecretKind.Password => "pwd",
        SecretKind.BearerToken => "brr",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown secret kind.")
    };
}
