using System.Globalization;
using System.Security.Cryptography;
using Dhadgar.Contracts.Agents;
using Dhadgar.Nodes.Services;
using Dhadgar.Nodes.Services.CommandSigning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Nodes.Tests.CommandSigning;

public sealed class CommandSignerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nodes-signing-" + Guid.NewGuid().ToString("N"));

    public CommandSignerTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the temp directory is removed by the OS eventually.
        }
    }

    [Fact]
    public async Task Signature_Verifies_OverSigningInput()
    {
        var signer = await CreateSignerAsync(Path.Combine(_root, "key.pem"));
        var signed = signer.Sign(SampleEnvelope());

        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(signer.PublicKeySpkiBase64), out _);
        var input = AgentCommandSigning.BuildSigningInput(signed);

        Assert.True(verifier.VerifyData(
            input,
            Convert.FromBase64String(signed.Signature),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.Equal(signer.KeyId, signed.SigningKeyId);
    }

    [Fact]
    public async Task Signature_HasFixedLengthEncoding()
    {
        var signer = await CreateSignerAsync(Path.Combine(_root, "key.pem"));
        var signed = signer.Sign(SampleEnvelope());

        Assert.Equal(AgentCommandSigning.SignatureBase64Length, signed.Signature.Length);
        Assert.Equal(AgentCommandSigning.SignatureBytes, Convert.FromBase64String(signed.Signature).Length);
    }

    [Fact]
    public async Task KeyId_IsHashOfPublicKeyInfo()
    {
        var signer = await CreateSignerAsync(Path.Combine(_root, "key.pem"));
        var spki = Convert.FromBase64String(signer.PublicKeySpkiBase64);

        Assert.Equal(AgentCommandSigning.ComputeKeyId(spki), signer.KeyId);
        Assert.Equal(AgentCommandSigning.KeyIdLength, signer.KeyId.Length);
    }

    [Fact]
    public async Task SecondSigner_OverSameFile_LoadsSameKey()
    {
        var path = Path.Combine(_root, "key.pem");
        var first = await CreateSignerAsync(path);
        var second = await CreateSignerAsync(path);

        Assert.Equal(first.KeyId, second.KeyId);
        Assert.Equal(first.PublicKeySpkiBase64, second.PublicKeySpkiBase64);
    }

    [Fact]
    public async Task CreatedKeyFile_IsPkcs8PrivateKeyPem()
    {
        var path = Path.Combine(_root, "key.pem");
        await CreateSignerAsync(path);

        var pem = await File.ReadAllTextAsync(path);
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", pem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task P384Key_FailsInitialization()
    {
        var path = Path.Combine(_root, "p384.pem");
        using (var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384))
        {
            await File.WriteAllTextAsync(path, p384.ExportPkcs8PrivateKeyPem());
        }

        var signer = CreateSigner(path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => signer.InitializeAsync());
    }

    [Fact]
    public void RelativeKeyPath_FailsValidation()
    {
        var options = new CommandSigningOptions { KeyPath = "keys/command-signing-key.pem" };

        Assert.True(options.Validate(null, options).Failed);
    }

    [Fact]
    public void KeyPathWithParentSegment_FailsValidation()
    {
        var options = new CommandSigningOptions { KeyPath = Path.Combine(_root, "..", "key.pem") };

        Assert.True(options.Validate(null, options).Failed);
    }

    [Theory]
    [InlineData(61, false)]
    [InlineData(9, false)]
    [InlineData(60, true)]
    [InlineData(10, true)]
    public void CommandTtlSeconds_IsBoundedToAgentLimit(int ttl, bool valid)
    {
        var options = new CommandSigningOptions { CommandTtlSeconds = ttl };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var ok = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            options,
            new System.ComponentModel.DataAnnotations.ValidationContext(options),
            results,
            validateAllProperties: true);

        Assert.Equal(valid, ok);
    }

    private async Task<ICommandSigner> CreateSignerAsync(string keyPath)
    {
        var signer = CreateSigner(keyPath);
        await signer.InitializeAsync();
        return signer;
    }

    private CommandSigner CreateSigner(string keyPath) =>
        new(
            Options.Create(new CommandSigningOptions { KeyPath = keyPath }),
            Options.Create(new NodesOptions { CaStoragePath = _root }),
            NullLogger<CommandSigner>.Instance);

    private static AgentCommandEnvelope SampleEnvelope() => new(
        CommandId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
        CommandType: AgentCommandTypes.Ping,
        NodeId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
        OrganizationId: Guid.Parse("77777777-7777-7777-7777-777777777777"),
        InitiatedByUserId: null,
        IssuedAt: DateTimeOffset.Parse("2026-10-26T12:00:00Z", CultureInfo.InvariantCulture),
        ExpiresAt: DateTimeOffset.Parse("2026-10-26T12:00:30Z", CultureInfo.InvariantCulture),
        PayloadJson: "{}",
        Signature: string.Empty,
        SigningKeyId: string.Empty,
        CorrelationId: "corr-test");
}