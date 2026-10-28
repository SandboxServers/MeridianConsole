using System.Security.Cryptography;
using System.Text;

namespace Dhadgar.Lab.Random;

/// <summary>
/// Seeded, replayable random source (C17). A stream is SplitMix64 seeded with <c>runSeed ^ FNV-1a-64(stream)</c>, so the same
/// seed and stream give the same sequence on every OS. Secrets never come from here; they use <see cref="RandomNumberGenerator"/>.
/// </summary>
public sealed class LabRandom
{
    private const ulong Increment = 0x9E3779B97F4A7C15UL;
    private const ulong Mix1 = 0xBF58476D1CE4E5B9UL;
    private const ulong Mix2 = 0x94D049BB133111EBUL;
    private const ulong FnvOffset = 0xCBF29CE484222325UL;
    private const ulong FnvPrime = 0x100000001B3UL;

    private ulong _state;

    /// <summary>
    /// Creates the stream <paramref name="stream"/> of the run seed <paramref name="runSeed"/>.
    /// </summary>
    /// <param name="runSeed">The run seed.</param>
    /// <param name="stream">The stream name, for example <c>runner</c> or <c>fault:&lt;id&gt;</c>.</param>
    public LabRandom(ulong runSeed, string stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _state = runSeed ^ Fnv1a64(stream);
    }

    /// <summary>
    /// Returns the next 64-bit value of the stream.
    /// </summary>
    public ulong NextUInt64()
    {
        _state += Increment;
        ulong z = _state;
        z = (z ^ (z >> 30)) * Mix1;
        z = (z ^ (z >> 27)) * Mix2;
        return z ^ (z >> 31);
    }

    /// <summary>
    /// Returns an unbiased integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).
    /// </summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive");
        }

        ulong range = (ulong)((long)maxExclusive - minInclusive);
        ulong limit = ulong.MaxValue - (ulong.MaxValue % range);
        ulong value;
        do
        {
            value = NextUInt64();
        }
        while (value >= limit);

        return minInclusive + (int)(value % range);
    }

    /// <summary>
    /// Returns a double in [0, 1) built from the top 53 bits.
    /// </summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>
    /// Fills <paramref name="buffer"/> with little-endian 64-bit values of the stream.
    /// </summary>
    public void NextBytes(Span<byte> buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            ulong value = NextUInt64();
            for (int i = 0; i < 8 && offset < buffer.Length; i++, offset++)
            {
                buffer[offset] = (byte)(value >> (8 * i));
            }
        }
    }

    /// <summary>
    /// Returns a fresh run seed from <see cref="RandomNumberGenerator"/>, the only non-seeded source.
    /// </summary>
    public static ulong NewSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private static ulong Fnv1a64(string text)
    {
        ulong hash = FnvOffset;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            hash = (hash ^ b) * FnvPrime;
        }

        return hash;
    }
}
