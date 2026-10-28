using Dhadgar.Lab.Data;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Stack;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// The I2/I3 applicability gate and the I5 stack check, before any stack exists (D-ML80). No network is used.
/// </summary>
public sealed class InvariantWiringGateTests
{
    [Fact]
    public async Task DbGate_NoCredentials_IsStackNotUp()
    {
        using var vault = new SecretVault();
        var refs = new LabCredentialRefs();
        var db = new LabDb(new LabConnectionStrings(vault, refs));
        InvariantDbGate gate = InvariantWiring.DbGate(db, refs);

        Result<DbApplicability> answer = await gate("nodes", CancellationToken.None);

        Assert.True(answer.IsSuccess);
        Assert.Equal(DbApplicability.StackNotUp, answer.Value);
    }

    [Fact]
    public void StackCreated_NoCredentials_IsFalse()
    {
        var refs = new LabCredentialRefs();

        Assert.False(InvariantWiring.StackCreated(refs)());
    }
}
