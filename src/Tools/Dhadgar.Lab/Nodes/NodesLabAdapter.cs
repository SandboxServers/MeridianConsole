using Dhadgar.Lab.Secrets;
using Dhadgar.Nodes;
using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Services;
using Dhadgar.Shared.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dhadgar.Lab.Nodes;

/// <summary>
/// Nodes product adapter for the lab. Mints enrollment tokens through the product's <see cref="EnrollmentTokenService"/>
/// and shifts certificate times on the product's <c>AgentCertificates</c> rows. Each call opens its own
/// <see cref="NodesDbContext"/> and disposes it; the adapter holds none.
/// </summary>
public sealed class NodesLabAdapter : INodesLabAdapter
{
    private readonly Func<NodesDbContext> _createContext;
    private readonly SecretVault _vault;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;

    /// <summary>
    /// Creates the adapter. <paramref name="createContext"/> returns a fresh context on each call.
    /// </summary>
    public NodesLabAdapter(Func<NodesDbContext> createContext, SecretVault vault, TimeProvider time, ILoggerFactory loggers)
    {
        _createContext = createContext ?? throw new ArgumentNullException(nameof(createContext));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
    }

    /// <inheritdoc />
    public async Task<Result<MintedToken>> MintTokenAsync(Guid organizationId, string label, TimeSpan validity, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (validity <= TimeSpan.Zero)
        {
            return Result<MintedToken>.Failure("[Lab.InvalidArgument] token validity must be positive");
        }

        try
        {
            await using NodesDbContext db = _createContext();
            var audit = new AuditService(
                db,
                new AuditContextAccessor(new HttpContextAccessor()),
                _time,
                NullLogger<AuditService>.Instance);
            var tokens = new EnrollmentTokenService(
                db,
                audit,
                _time,
                Options.Create(new NodesOptions()),
                _loggers.CreateLogger<EnrollmentTokenService>());

            (Dhadgar.Nodes.Data.Entities.EnrollmentToken token, string plainText) =
                await tokens.CreateTokenAsync(organizationId, LabIds.CreatedByUserId, label, validity, ct);

            // The plain token is copied into the vault and the local buffer is zeroed. The string returned by the
            // product cannot be zeroed (strings are immutable); it is not kept in any field or log.
            char[] buffer = plainText.ToCharArray();
            string tokenRef;
            try
            {
                tokenRef = _vault.Add(SecretKind.EnrollmentToken, buffer);
            }
            finally
            {
                Array.Clear(buffer);
            }

            var expiresAt = new DateTimeOffset(DateTime.SpecifyKind(token.ExpiresAt, DateTimeKind.Utc));
            return Result<MintedToken>.Success(new MintedToken(token.Id, tokenRef, expiresAt));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<MintedToken>.Failure($"[Lab.Failed] nodes token mint failed: {ex.GetType().Name}");
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> ShiftCertificateTimesAsync(Guid nodeId, TimeSpan shift, IReadOnlyCollection<CertTimeField> fields, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (shift > TimeSpan.Zero)
        {
            return Result<int>.Failure("[Lab.InvalidArgument] shift must not be positive");
        }

        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count == 0)
        {
            return Result<int>.Failure("[Lab.InvalidArgument] at least one certificate field is required");
        }

        try
        {
            await using NodesDbContext db = _createContext();
            var rows = await db.AgentCertificates
                .Where(c => c.NodeId == nodeId)
                .ToListAsync(ct);

            var updated = 0;
            foreach (var row in rows)
            {
                foreach (CertTimeField field in fields.Distinct())
                {
                    switch (field)
                    {
                        case CertTimeField.IssuedAt:
                            row.IssuedAt = row.IssuedAt.Add(shift);
                            break;
                        case CertTimeField.NotBefore:
                            row.NotBefore = row.NotBefore.Add(shift);
                            break;
                        case CertTimeField.NotAfter:
                            row.NotAfter = row.NotAfter.Add(shift);
                            break;
                        case CertTimeField.GraceUntil:
                            // A null GraceUntil stays null.
                            if (row.GraceUntil is not null)
                            {
                                row.GraceUntil = row.GraceUntil.Value.Add(shift);
                            }

                            break;
                        default:
                            return Result<int>.Failure("[Lab.InvalidArgument] unknown certificate field");
                    }
                }

                updated++;
            }

            await db.SaveChangesAsync(ct);
            return Result<int>.Success(updated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<int>.Failure($"[Lab.Failed] nodes certificate shift failed: {ex.GetType().Name}");
        }
    }
}
