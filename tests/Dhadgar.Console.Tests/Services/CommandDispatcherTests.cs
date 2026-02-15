using Dhadgar.Console.Data;
using Dhadgar.Console.Services;
using Dhadgar.Contracts.Console;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dhadgar.Console.Tests.Services;

public class CommandDispatcherTests
{
    private readonly ConsoleDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly CommandDispatcher _dispatcher;
    private readonly FakeTimeProvider _timeProvider;

    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    public CommandDispatcherTests()
    {
        var options = new DbContextOptionsBuilder<ConsoleDbContext>()
            .UseInMemoryDatabase($"cmd-test-{Guid.NewGuid()}")
            .Options;
        _db = new ConsoleDbContext(options);
        _publishEndpoint = Substitute.For<IPublishEndpoint>();
        var consoleOptions = Options.Create(new ConsoleOptions());
        var logger = Substitute.For<ILogger<CommandDispatcher>>();
        _timeProvider = new FakeTimeProvider();
        _dispatcher = new CommandDispatcher(_db, _publishEndpoint, consoleOptions, logger, _timeProvider);
    }

    [Fact]
    public async Task DispatchCommand_AllowedCommand_ReturnsSuccess()
    {
        var result = await _dispatcher.DispatchCommandAsync(
            ServerId, OrgId, "say hello world", UserId, "testuser", "conn1", "iphash1");

        result.IsSuccess.Should().BeTrue();
        result.Value.ServerId.Should().Be(ServerId);
        result.Value.Command.Should().Be("say hello world");
        result.Value.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchCommand_AllowedCommand_CreatesAuditLog()
    {
        await _dispatcher.DispatchCommandAsync(
            ServerId, OrgId, "say hello world", UserId, "testuser", "conn1", "iphash1");

        var auditLog = await _db.CommandAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.ServerId.Should().Be(ServerId);
        auditLog.OrganizationId.Should().Be(OrgId);
        auditLog.WasAllowed.Should().BeTrue();
        auditLog.Command.Should().Be("say hello world");
    }

    [Fact]
    public async Task DispatchCommand_AllowedCommand_PublishesEvent()
    {
        await _dispatcher.DispatchCommandAsync(
            ServerId, OrgId, "say hello world", UserId, "testuser", "conn1", "iphash1");

        await _publishEndpoint.Received(1).Publish(
            Arg.Is<ExecuteServerCommand>(cmd =>
                cmd.ServerId == ServerId &&
                cmd.OrganizationId == OrgId &&
                cmd.Command == "say hello world" &&
                cmd.UserId == UserId &&
                cmd.Username == "testuser"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchCommand_BlockedCommand_ReturnsFailure()
    {
        var result = await _dispatcher.DispatchCommandAsync(
            ServerId, OrgId, "rm -rf /", UserId, "testuser", "conn1", "iphash1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("does not match any allowed pattern");
    }

    [Fact]
    public async Task DispatchCommand_BlockedCommand_LogsBlocked()
    {
        await _dispatcher.DispatchCommandAsync(
            ServerId, OrgId, "rm -rf /", UserId, "testuser", "conn1", "iphash1");

        var auditLog = await _db.CommandAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.WasAllowed.Should().BeFalse();
        auditLog.BlockReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DispatchCommand_EmptyCommand_ReturnsFailure()
    {
        var result = await _dispatcher.DispatchCommandAsync(
            ServerId, OrgId, "", UserId, "testuser", "conn1", "iphash1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Command cannot be empty");
    }

    [Fact]
    public async Task DispatchCommand_TooLongCommand_ReturnsFailure()
    {
        var longCommand = new string('a', 2001);

        var result = await _dispatcher.DispatchCommandAsync(
            ServerId, OrgId, longCommand, UserId, "testuser", "conn1", "iphash1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("exceeds maximum length");
    }

    [Fact]
    public async Task DispatchCommand_SlashCommand_Allowed()
    {
        var result = await _dispatcher.DispatchCommandAsync(
            ServerId, OrgId, "/say hello", UserId, "testuser", "conn1", "iphash1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Command.Should().Be("/say hello");
    }

    [Fact]
    public async Task DispatchCommand_NonSlashCommand_Allowed()
    {
        var result = await _dispatcher.DispatchCommandAsync(
            ServerId, OrgId, "say hello", UserId, "testuser", "conn1", "iphash1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Command.Should().Be("say hello");
    }

    [Fact]
    public async Task DispatchCommand_UnknownCommand_Blocked()
    {
        var result = await _dispatcher.DispatchCommandAsync(
            ServerId, OrgId, "unknown_cmd", UserId, "testuser", "conn1", "iphash1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("does not match any allowed pattern");
    }

    [Fact]
    public async Task DispatchCommand_LeadingWhitespace_Normalized()
    {
        var result = await _dispatcher.DispatchCommandAsync(
            ServerId, OrgId, "  say hello", UserId, "testuser", "conn1", "iphash1");

        result.IsSuccess.Should().BeTrue();
    }
}
