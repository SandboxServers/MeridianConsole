using System.Security.Cryptography;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// A stable agent key pair for tests, generated the same way Dhadgar.Agent.Core does
/// (ECDSA P-384, SubjectPublicKeyInfo, base64).
/// </summary>
internal static class TestAgentKeys
{
    private static readonly Lazy<ECDsa> Key = new(() => ECDsa.Create(ECCurve.NamedCurves.nistP384));

    public static ECDsa PrivateKey => Key.Value;

    public static string PublicKeyBase64 => Convert.ToBase64String(Key.Value.ExportSubjectPublicKeyInfo());
}
