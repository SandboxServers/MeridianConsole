using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Services;
using Dhadgar.Nodes;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Windows.Uat.Stack;

/// <summary>A minted enrollment token. Only the ID is printed; the plain text is for the enroll request and is redacted.</summary>
internal sealed record MintedToken(Guid Id, string PlainText)
{
    /// <summary>Prints the token ID only, so the plain text never reaches a log line.</summary>
    public override string ToString() => Id.ToString();
}

/// <summary>
/// Mints enrollment tokens through the real Nodes <see cref="EnrollmentTokenService"/> (P19, S-mint).
/// The plain text is added to the run's redactor at mint time.
/// </summary>
internal sealed class TokenMinter
{
    private readonly Func<NodesDbContext> _createContext;
    private readonly string _runId;
    private readonly Redactor _redactor;

    /// <summary>Creates a minter over a context factory (Postgres in a run, InMemory in tests).</summary>
    public TokenMinter(Func<NodesDbContext> createContext, string runId, Redactor redactor)
    {
        _createContext = createContext;
        _runId = runId;
        _redactor = redactor;
    }

    /// <summary>
    /// Creates a single-use token for <paramref name="organizationId"/>. <paramref name="lifetime"/> defaults to 30 minutes;
    /// the MSI leg passes 10 minutes (P23).
    /// </summary>
    public async Task<Result<MintedToken>> MintAsync(Guid organizationId, string label, CancellationToken ct, TimeSpan? lifetime = null)
    {
        try
        {
            await using var db = _createContext();
            var service = new EnrollmentTokenService(
                db, new NoopAuditService(), TimeProvider.System,
                Options.Create(new NodesOptions()), NullLogger<EnrollmentTokenService>.Instance);
            var (token, plainText) = await service.CreateTokenAsync(
                organizationId, createdByUserId: $"uat-{_runId}", label, lifetime ?? TimeSpan.FromMinutes(30), ct);
            _redactor.AddSecret(plainText, SecretKind.EnrollmentToken);
            return Result<MintedToken>.Success(new MintedToken(token.Id, plainText));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<MintedToken>.Failure($"token mint failed: {ex.GetType().Name}");
        }
    }
}
