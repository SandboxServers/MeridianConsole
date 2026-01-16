using System.Collections.Concurrent;
using Dhadgar.Contracts.Identity;
using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Tests;

public sealed class TestIdentityEventPublisher : IIdentityEventPublisher
{
    public ConcurrentQueue<UserAuthenticated> UserAuthenticatedEvents { get; } = new();
    public ConcurrentQueue<OrgMembershipChanged> OrgMembershipChangedEvents { get; } = new();
    public ConcurrentQueue<UserDeactivated> UserDeactivatedEvents { get; } = new();

    public Task PublishUserAuthenticatedAsync(UserAuthenticated message, CancellationToken ct = default)
    {
        UserAuthenticatedEvents.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task PublishOrgMembershipChangedAsync(OrgMembershipChanged message, CancellationToken ct = default)
    {
        OrgMembershipChangedEvents.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task PublishUserDeactivatedAsync(UserDeactivated message, CancellationToken ct = default)
    {
        UserDeactivatedEvents.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        while (UserAuthenticatedEvents.TryDequeue(out _))
        {
        }

        while (OrgMembershipChangedEvents.TryDequeue(out _))
        {
        }

        while (UserDeactivatedEvents.TryDequeue(out _))
        {
        }
    }
}
