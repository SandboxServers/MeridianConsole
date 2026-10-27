using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Certificate identity checks shared by enrollment, renewal and the bootstrap service (D-S232, A2).
/// </summary>
internal static class AgentCertificateIdentity
{
    /// <summary>How far a certificate's NotBefore may sit ahead of this machine's clock (Nodes stamps it on its own clock).</summary>
    internal const int MaxClockSkewMinutes = 5;

    /// <summary>
    /// True when <paramref name="certificate"/> is usable at <paramref name="now"/>, allowing NotBefore up to
    /// <see cref="MaxClockSkewMinutes"/> ahead. The one validity rule for enrollment, renewal and the stores (D-S235).
    /// </summary>
    internal static bool IsWithinValidity(X509Certificate2 certificate, DateTimeOffset now) =>
        new DateTimeOffset(certificate.NotBefore) <= now.AddMinutes(MaxClockSkewMinutes)
        && now < new DateTimeOffset(certificate.NotAfter);

    /// <summary>
    /// True when the certificate's only URI SAN is <paramref name="expectedUri"/>.
    /// </summary>
    internal static bool HasOnlySpiffeUri(X509Certificate2 certificate, string expectedUri)
    {
        var uriCount = 0;
        var matches = false;

        foreach (var extension in certificate.Extensions)
        {
            // subjectAltName
            if (extension.Oid?.Value != "2.5.29.17")
            {
                continue;
            }

            try
            {
                var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
                var names = reader.ReadSequence();
                while (names.HasData)
                {
                    // GeneralName uniformResourceIdentifier is [6] IA5String
                    if (names.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 6)))
                    {
                        var uri = names.ReadCharacterString(
                            UniversalTagNumber.IA5String,
                            new Asn1Tag(TagClass.ContextSpecific, 6));
                        uriCount++;
                        matches = string.Equals(uri, expectedUri, StringComparison.Ordinal);
                    }
                    else
                    {
                        names.ReadEncodedValue();
                    }
                }
            }
            catch (AsnContentException)
            {
                // A malformed SAN is a wrong identity, not a crash. AsnContentException derives from
                // Exception, not CryptographicException, so the caller's catch clauses would not see it.
                return false;
            }
        }

        return uriCount == 1 && matches;
    }

    /// <summary>
    /// Reads the URI names in the certificate's subjectAltName. Returns false when the SAN is malformed; then
    /// <paramref name="names"/> holds only the names read before the malformed element. Identity checks must treat
    /// false as a failed check, so a truncated SAN can never classify a certificate.
    /// </summary>
    internal static bool TryReadUriNames(X509Certificate2 certificate, out IReadOnlyList<string> names)
    {
        var read = new List<string>();
        names = read;
        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid?.Value != "2.5.29.17")
            {
                continue;
            }

            try
            {
                var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
                var sequence = reader.ReadSequence();
                while (sequence.HasData)
                {
                    if (sequence.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 6)))
                    {
                        read.Add(sequence.ReadCharacterString(
                            UniversalTagNumber.IA5String,
                            new Asn1Tag(TagClass.ContextSpecific, 6)));
                    }
                    else
                    {
                        sequence.ReadEncodedValue();
                    }
                }
            }
            catch (AsnContentException)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the URI names in the certificate's subjectAltName, stopping at a malformed element. Used only for the
    /// bootstrap mismatch log, which names the certificate's node. Identity checks use <see cref="TryReadUriNames"/>.
    /// </summary>
    internal static IReadOnlyList<string> ReadUriNames(X509Certificate2 certificate)
    {
        TryReadUriNames(certificate, out var names);
        return names;
    }
}