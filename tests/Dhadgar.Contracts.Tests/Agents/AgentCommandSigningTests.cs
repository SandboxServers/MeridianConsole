using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dhadgar.Contracts.Agents;
using Xunit;

namespace Dhadgar.Contracts.Tests.Agents;

public class AgentCommandSigningTests
{
    private static readonly Guid CommandId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset IssuedAt = DateTimeOffset.Parse("2026-10-26T12:00:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset ExpiresAt = IssuedAt.AddSeconds(60);
    private const string PayloadJson = "{\"nonce\":\"abc\"}";

    private const string ExpectedInput =
        "dhadgar-cmd-v1\n" +
        "11111111-1111-1111-1111-111111111111\n" +
        "22222222-2222-2222-2222-222222222222\n" +
        "33333333-3333-3333-3333-333333333333\n" +
        "Ping\n" +
        "1793016000000\n" +
        "1793016060000\n" +
        "\n" +
        "corr-1\n" +
        "7685965bd35af178255728a43edb00ed348b2717b663ffc7376a547e131590f9";

    [Fact]
    public void BuildSigningInput_MatchesGoldenVector()
    {
        var input = AgentCommandSigning.BuildSigningInput(
            CommandId, NodeId, OrganizationId, "Ping",
            IssuedAt, ExpiresAt, null, "corr-1", PayloadJson);

        Assert.Equal(ExpectedInput, Encoding.UTF8.GetString(input));
        Assert.Equal(
            "723f9365d61f8ac48c6acb7801e582204a05fc72e9a4fbfcb0467ca9add11c3a",
            Convert.ToHexStringLower(SHA256.HashData(input)));
    }

    [Fact]
    public void ComputeKeyId_MatchesGoldenVector()
    {
        var spki = Enumerable.Range(0x01, 0x5B).Select(i => (byte)i).ToArray();

        var keyId = AgentCommandSigning.ComputeKeyId(spki);

        Assert.Equal("4678fe8a3cf5c74e029e7249e4313c06ab59cb2b07d6a7ab15c6d1850c2c8e25", keyId);
    }

    [Fact]
    public void SignAndVerify_RoundTrips()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var input = AgentCommandSigning.BuildSigningInput(
            CommandId, NodeId, OrganizationId, "Ping",
            IssuedAt, ExpiresAt, null, "corr-1", PayloadJson);

        var signature = ecdsa.SignData(input, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var verified = ecdsa.VerifyData(input, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        Assert.True(verified);

        var tampered = AgentCommandSigning.BuildSigningInput(
            CommandId, NodeId, OrganizationId, "Ping",
            IssuedAt, ExpiresAt, null, "corr-1", "{\"nonce\":\"abd\"}");
        var tamperedVerified = ecdsa.VerifyData(tampered, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        Assert.False(tamperedVerified);
    }

    [Fact]
    public void BuildSigningInput_RejectsLineBreaks()
    {
        Assert.Throws<ArgumentException>(() => AgentCommandSigning.BuildSigningInput(
            CommandId, NodeId, OrganizationId, "Ping\nStopServer",
            IssuedAt, ExpiresAt, null, "corr-1", PayloadJson));

        Assert.Throws<ArgumentException>(() => AgentCommandSigning.BuildSigningInput(
            CommandId, NodeId, OrganizationId, "Ping",
            IssuedAt, ExpiresAt, null, "corr\r1", PayloadJson));
    }

    [Fact]
    public void EnvelopeOverload_EqualsFieldOverload()
    {
        var envelope = new AgentCommandEnvelope(
            CommandId, "Ping", NodeId, OrganizationId, null,
            IssuedAt, ExpiresAt, PayloadJson, "sig", "kid", "corr-1");

        var fromEnvelope = AgentCommandSigning.BuildSigningInput(envelope);
        var fromFields = AgentCommandSigning.BuildSigningInput(
            CommandId, NodeId, OrganizationId, "Ping",
            IssuedAt, ExpiresAt, null, "corr-1", PayloadJson);

        Assert.Equal(fromFields, fromEnvelope);
    }
}
