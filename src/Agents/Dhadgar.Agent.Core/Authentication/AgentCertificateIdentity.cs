using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Certificate identity checks shared by enrollment, renewal and the bootstrap service (D-S232, A2).
/// </summary>
internal static class AgentCertificateIdentity
{
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
    /// Reads the URI names in the certificate's subjectAltName, for diagnostics only (a mismatch log names the certificate's node).
    /// </summary>
    internal static IReadOnlyList<string> ReadUriNames(X509Certificate2 certificate)
    {
        var names = new List<string>();
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
                        names.Add(sequence.ReadCharacterString(
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
                // Stop at a malformed SAN; the names read so far are still useful for the log.
                break;
            }
        }

        return names;
    }
}