using Dhadgar.Contracts.Identity;
using MassTransit;

namespace Dhadgar.Identity.Services;

public interface IIdentityEventPublisher
{
    Task PublishUserAuthenticatedAsync(UserAuthenticated message, CancellationToken ct = default);
    Task PublishOrgMembershipChangedAsync(OrgMembershipChanged message, CancellationToken ct = default);
    Task PublishUserDeactivatedAsync(UserDeactivated message, CancellationToken ct = default);
}

public sealed class IdentityEventPublisher : IIdentityEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public IdentityEventPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public Task PublishUserAuthenticatedAsync(UserAuthenticated message, CancellationToken ct = default)
        => _publishEndpoint.Publish(message, ct);

    public Task PublishOrgMembershipChangedAsync(OrgMembershipChanged message, CancellationToken ct = default)
        => _publishEndpoint.Publish(message, ct);

    public Task PublishUserDeactivatedAsync(UserDeactivated message, CancellationToken ct = default)
        => _publishEndpoint.Publish(message, ct);
}
