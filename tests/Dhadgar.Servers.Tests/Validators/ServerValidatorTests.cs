using Dhadgar.Contracts.Servers;
using Dhadgar.Servers.Validators;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Dhadgar.Servers.Tests.Validators;

public sealed class CreateServerRequestValidatorTests
{
    private readonly CreateServerRequestValidator _validator = new();

    private static CreateServerRequest ValidRequest() => new(
        Name: "my-server",
        DisplayName: "My Server",
        GameType: "minecraft",
        CpuLimitMillicores: 2000,
        MemoryLimitMb: 4096,
        DiskLimitMb: 10240,
        TemplateId: null,
        StartupCommand: null,
        GameSettings: null,
        AutoStart: false,
        AutoRestartOnCrash: false,
        Ports: null,
        Tags: null);

    [Fact]
    public void Validate_ValidRequest_ShouldPass()
    {
        var result = _validator.TestValidate(ValidRequest());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_ShouldFail()
    {
        var request = ValidRequest() with { Name = "" };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NameTooLong_ShouldFail()
    {
        var request = ValidRequest() with { Name = new string('a', 101) };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Theory]
    [InlineData("My-Server")]
    [InlineData("-server")]
    [InlineData("server-")]
    [InlineData("my server")]
    [InlineData("my_server")]
    [InlineData("SERVER")]
    public void Validate_InvalidNamePattern_ShouldFail(string name)
    {
        var request = ValidRequest() with { Name = name };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("my-server")]
    [InlineData("server1")]
    [InlineData("a1b2c3")]
    public void Validate_ValidNamePattern_ShouldPass(string name)
    {
        var request = ValidRequest() with { Name = name };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_EmptyGameType_ShouldFail()
    {
        var request = ValidRequest() with { GameType = "" };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.GameType);
    }

    [Fact]
    public void Validate_GameTypeTooLong_ShouldFail()
    {
        var request = ValidRequest() with { GameType = new string('x', 51) };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.GameType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_CpuLimitNotPositive_ShouldFail(int cpu)
    {
        var request = ValidRequest() with { CpuLimitMillicores = cpu };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.CpuLimitMillicores);
    }

    [Fact]
    public void Validate_CpuLimitExceedsMax_ShouldFail()
    {
        var request = ValidRequest() with { CpuLimitMillicores = 64001 };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.CpuLimitMillicores);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_MemoryLimitNotPositive_ShouldFail(int mem)
    {
        var request = ValidRequest() with { MemoryLimitMb = mem };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.MemoryLimitMb);
    }

    [Fact]
    public void Validate_MemoryLimitExceedsMax_ShouldFail()
    {
        var request = ValidRequest() with { MemoryLimitMb = (1024 * 1024) + 1 };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.MemoryLimitMb);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_DiskLimitNotPositive_ShouldFail(int disk)
    {
        var request = ValidRequest() with { DiskLimitMb = disk };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DiskLimitMb);
    }

    [Fact]
    public void Validate_DiskLimitExceedsMax_ShouldFail()
    {
        var request = ValidRequest() with { DiskLimitMb = (10 * 1024 * 1024) + 1 };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DiskLimitMb);
    }

    [Fact]
    public void Validate_TooManyTags_ShouldFail()
    {
        var tags = Enumerable.Range(0, 21).Select(i => $"tag{i}").ToList();
        var request = ValidRequest() with { Tags = tags };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Tags);
    }

    [Fact]
    public void Validate_TagTooLong_ShouldFail()
    {
        var request = ValidRequest() with { Tags = [new string('t', 51)] };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor("Tags[0]");
    }

    [Fact]
    public void Validate_ValidTags_ShouldPass()
    {
        var request = ValidRequest() with { Tags = ["survival", "pvp", "modded"] };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Tags);
    }

    [Fact]
    public void Validate_StartupCommandTooLong_ShouldFail()
    {
        var request = ValidRequest() with { StartupCommand = new string('c', 2001) };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.StartupCommand);
    }
}

public sealed class CreateServerPortRequestValidatorTests
{
    private readonly CreateServerPortRequestValidator _validator = new();

    private static CreateServerPortRequest ValidPort() => new(
        Name: "game",
        Protocol: "tcp",
        InternalPort: 25565,
        ExternalPort: null,
        IsPrimary: true);

    [Fact]
    public void Validate_ValidPort_ShouldPass()
    {
        var result = _validator.TestValidate(ValidPort());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_ShouldFail()
    {
        var port = ValidPort() with { Name = "" };
        var result = _validator.TestValidate(port);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NameTooLong_ShouldFail()
    {
        var port = ValidPort() with { Name = new string('n', 51) };
        var result = _validator.TestValidate(port);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_EmptyProtocol_ShouldFail()
    {
        var port = ValidPort() with { Protocol = "" };
        var result = _validator.TestValidate(port);
        result.ShouldHaveValidationErrorFor(x => x.Protocol);
    }

    [Fact]
    public void Validate_InvalidProtocol_ShouldFail()
    {
        var port = ValidPort() with { Protocol = "icmp" };
        var result = _validator.TestValidate(port);
        result.ShouldHaveValidationErrorFor(x => x.Protocol);
    }

    [Theory]
    [InlineData("tcp")]
    [InlineData("udp")]
    [InlineData("TCP")]
    [InlineData("UDP")]
    public void Validate_ValidProtocol_ShouldPass(string protocol)
    {
        var port = ValidPort() with { Protocol = protocol };
        var result = _validator.TestValidate(port);
        result.ShouldNotHaveValidationErrorFor(x => x.Protocol);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_InternalPortOutOfRange_ShouldFail(int internalPort)
    {
        var port = ValidPort() with { InternalPort = internalPort };
        var result = _validator.TestValidate(port);
        result.ShouldHaveValidationErrorFor(x => x.InternalPort);
    }

    [Fact]
    public void Validate_ExternalPortOutOfRange_ShouldFail()
    {
        var port = ValidPort() with { ExternalPort = 0 };
        var result = _validator.TestValidate(port);
        result.ShouldHaveValidationErrorFor(x => x.ExternalPort);
    }

    [Fact]
    public void Validate_ValidExternalPort_ShouldPass()
    {
        var port = ValidPort() with { ExternalPort = 25565 };
        var result = _validator.TestValidate(port);
        result.ShouldNotHaveValidationErrorFor(x => x.ExternalPort);
    }
}

public sealed class UpdateServerRequestValidatorTests
{
    private readonly UpdateServerRequestValidator _validator = new();

    [Fact]
    public void Validate_AllNull_ShouldPass()
    {
        var request = new UpdateServerRequest(null, null, null);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ValidName_ShouldPass()
    {
        var request = new UpdateServerRequest("new-name", null, null);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_InvalidNamePattern_ShouldFail()
    {
        var request = new UpdateServerRequest("INVALID", null, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NameTooLong_ShouldFail()
    {
        var request = new UpdateServerRequest(new string('a', 101), null, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_DisplayNameTooLong_ShouldFail()
    {
        var request = new UpdateServerRequest(null, new string('d', 201), null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }

    [Fact]
    public void Validate_TooManyTags_ShouldFail()
    {
        var tags = Enumerable.Range(0, 21).Select(i => $"tag{i}").ToList();
        var request = new UpdateServerRequest(null, null, tags);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Tags);
    }

    [Fact]
    public void Validate_TagTooLong_ShouldFail()
    {
        var request = new UpdateServerRequest(null, null, [new string('t', 51)]);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor("Tags[0]");
    }
}

public sealed class CreateServerTemplateRequestValidatorTests
{
    private readonly CreateServerTemplateRequestValidator _validator = new();

    private static CreateServerTemplateRequest ValidRequest() => new(
        Name: "minecraft-standard",
        Description: "Standard Minecraft server template",
        GameType: "minecraft",
        IsPublic: false,
        DefaultCpuLimitMillicores: 2000,
        DefaultMemoryLimitMb: 4096,
        DefaultDiskLimitMb: 10240,
        DefaultStartupCommand: "java -jar server.jar",
        DefaultGameSettings: null,
        DefaultEnvironmentVariables: null,
        DefaultJavaFlags: "-Xmx4G -Xms2G",
        DefaultPorts: null);

    [Fact]
    public void Validate_ValidRequest_ShouldPass()
    {
        var result = _validator.TestValidate(ValidRequest());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_ShouldFail()
    {
        var request = ValidRequest() with { Name = "" };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NameTooLong_ShouldFail()
    {
        var request = ValidRequest() with { Name = new string('a', 101) };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_EmptyGameType_ShouldFail()
    {
        var request = ValidRequest() with { GameType = "" };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.GameType);
    }

    [Fact]
    public void Validate_DescriptionTooLong_ShouldFail()
    {
        var request = ValidRequest() with { Description = new string('d', 1001) };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Validate_CpuLimitZero_ShouldFail()
    {
        var request = ValidRequest() with { DefaultCpuLimitMillicores = 0 };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DefaultCpuLimitMillicores);
    }

    [Fact]
    public void Validate_CpuLimitExceedsMax_ShouldFail()
    {
        var request = ValidRequest() with { DefaultCpuLimitMillicores = 64001 };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DefaultCpuLimitMillicores);
    }

    [Fact]
    public void Validate_MemoryLimitZero_ShouldFail()
    {
        var request = ValidRequest() with { DefaultMemoryLimitMb = 0 };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DefaultMemoryLimitMb);
    }

    [Fact]
    public void Validate_DiskLimitExceedsMax_ShouldFail()
    {
        var request = ValidRequest() with { DefaultDiskLimitMb = (10 * 1024 * 1024) + 1 };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DefaultDiskLimitMb);
    }

    [Fact]
    public void Validate_StartupCommandTooLong_ShouldFail()
    {
        var request = ValidRequest() with { DefaultStartupCommand = new string('c', 2001) };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DefaultStartupCommand);
    }

    [Fact]
    public void Validate_JavaFlagsTooLong_ShouldFail()
    {
        var request = ValidRequest() with { DefaultJavaFlags = new string('j', 501) };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DefaultJavaFlags);
    }
}

public sealed class UpdateServerTemplateRequestValidatorTests
{
    private readonly UpdateServerTemplateRequestValidator _validator = new();

    [Fact]
    public void Validate_AllNull_ShouldPass()
    {
        var request = new UpdateServerTemplateRequest(
            null, null, null, null, null, null, null, null, null, null, null, null);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_ShouldFail()
    {
        var request = new UpdateServerTemplateRequest(
            "", null, null, null, null, null, null, null, null, null, null, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NameTooLong_ShouldFail()
    {
        var request = new UpdateServerTemplateRequest(
            new string('a', 101), null, null, null, null, null, null, null, null, null, null, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_DescriptionTooLong_ShouldFail()
    {
        var request = new UpdateServerTemplateRequest(
            null, new string('d', 1001), null, null, null, null, null, null, null, null, null, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Validate_CpuLimitZeroWhenPresent_ShouldFail()
    {
        var request = new UpdateServerTemplateRequest(
            null, null, null, null, 0, null, null, null, null, null, null, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DefaultCpuLimitMillicores);
    }

    [Fact]
    public void Validate_CpuLimitExceedsMaxWhenPresent_ShouldFail()
    {
        var request = new UpdateServerTemplateRequest(
            null, null, null, null, 64001, null, null, null, null, null, null, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DefaultCpuLimitMillicores);
    }

    [Fact]
    public void Validate_MemoryLimitZeroWhenPresent_ShouldFail()
    {
        var request = new UpdateServerTemplateRequest(
            null, null, null, null, null, 0, null, null, null, null, null, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DefaultMemoryLimitMb);
    }

    [Fact]
    public void Validate_DiskLimitExceedsMaxWhenPresent_ShouldFail()
    {
        var request = new UpdateServerTemplateRequest(
            null, null, null, null, null, null, (10 * 1024 * 1024) + 1, null, null, null, null, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DefaultDiskLimitMb);
    }

    [Fact]
    public void Validate_StartupCommandTooLong_ShouldFail()
    {
        var request = new UpdateServerTemplateRequest(
            null, null, null, null, null, null, null, new string('c', 2001), null, null, null, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DefaultStartupCommand);
    }

    [Fact]
    public void Validate_JavaFlagsTooLong_ShouldFail()
    {
        var request = new UpdateServerTemplateRequest(
            null, null, null, null, null, null, null, null, null, null, new string('j', 501), null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DefaultJavaFlags);
    }

    [Fact]
    public void Validate_ValidPartialUpdate_ShouldPass()
    {
        var request = new UpdateServerTemplateRequest(
            "new-name", "Updated description", true, false, 4000, 8192, 20480, null, null, null, null, null);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
