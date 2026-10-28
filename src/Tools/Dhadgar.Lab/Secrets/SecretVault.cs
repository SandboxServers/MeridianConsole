using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Secrets;

/// <summary>
/// Kinds of secret the vault holds. Each kind has its own reference prefix (tok-, key-, pwd-, brr-).
/// </summary>
public enum SecretKind { EnrollmentToken, PrivateKey, Password, BearerToken }

/// <summary>
/// Holds secrets as <c>char[]</c> and hands them out only by reference. L01 ships the contract; L04 implements storage and zeroing.
/// </summary>
public sealed class SecretVault : IDisposable
{
    /// <summary>
    /// Stores a value and returns its reference ("tok-1", "key-1", "pwd-1", "brr-1"). Not yet implemented.
    /// </summary>
    public string Add(SecretKind kind, ReadOnlySpan<char> value)
    {
        _ = kind;
        _ = value.Length;
        throw new NotImplementedException("[Lab.NotImplemented] secret vault (L04)");
    }

    /// <summary>
    /// Runs <paramref name="use"/> over the stored value without copying it out.
    /// </summary>
    public Result<T> Use<T>(string secretRef, Func<ReadOnlySpan<char>, T> use)
    {
        ArgumentNullException.ThrowIfNull(secretRef);
        ArgumentNullException.ThrowIfNull(use);

        return Result<T>.Failure("[Lab.NotImplemented] secret vault (L04)");
    }

    public bool Contains(string secretRef)
    {
        ArgumentNullException.ThrowIfNull(secretRef);
        return false;
    }

    /// <summary>
    /// Removes and zeroes a value. No-op until L04.
    /// </summary>
    public void Remove(string secretRef)
    {
        ArgumentNullException.ThrowIfNull(secretRef);
    }

    /// <summary>
    /// Zeroes every stored value. No-op until L04.
    /// </summary>
    public void Dispose()
    {
    }
}
