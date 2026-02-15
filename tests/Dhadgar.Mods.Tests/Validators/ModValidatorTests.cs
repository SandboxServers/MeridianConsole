using Dhadgar.Contracts.Mods;
using Dhadgar.Mods.Validators;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Dhadgar.Mods.Tests.Validators;

public class ModValidatorTests
{
    // ── CreateModRequestValidator ──────────────────────────────────────────

    private readonly CreateModRequestValidator _createValidator = new();

    [Fact]
    public void CreateMod_ValidRequest_PassesValidation()
    {
        var request = new CreateModRequest(
            Name: "My Cool Mod",
            Slug: "my-cool-mod",
            Description: "A really cool mod",
            Author: "TestAuthor",
            CategoryId: null,
            GameType: "minecraft",
            IsPublic: true,
            ProjectUrl: "https://github.com/test/mod",
            IconUrl: "https://cdn.example.com/icon.png",
            Tags: ["utility", "performance"]);

        var result = _createValidator.TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateMod_EmptyName_FailsValidation()
    {
        var request = new CreateModRequest(
            Name: "",
            Slug: "my-mod",
            Description: null,
            Author: null,
            CategoryId: null,
            GameType: "minecraft",
            IsPublic: false,
            ProjectUrl: null,
            IconUrl: null,
            Tags: null);

        var result = _createValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void CreateMod_NameExceedsMaxLength_FailsValidation()
    {
        var request = new CreateModRequest(
            Name: new string('a', 101),
            Slug: "my-mod",
            Description: null,
            Author: null,
            CategoryId: null,
            GameType: "minecraft",
            IsPublic: false,
            ProjectUrl: null,
            IconUrl: null,
            Tags: null);

        var result = _createValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void CreateMod_EmptySlug_FailsValidation()
    {
        var request = new CreateModRequest(
            Name: "My Mod",
            Slug: "",
            Description: null,
            Author: null,
            CategoryId: null,
            GameType: "minecraft",
            IsPublic: false,
            ProjectUrl: null,
            IconUrl: null,
            Tags: null);

        var result = _createValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Slug);
    }

    [Theory]
    [InlineData("my-mod")]
    [InlineData("a")]
    [InlineData("mod123")]
    [InlineData("a1b2c3")]
    public void CreateMod_ValidSlugs_PassValidation(string slug)
    {
        var request = new CreateModRequest(
            Name: "My Mod",
            Slug: slug,
            Description: null,
            Author: null,
            CategoryId: null,
            GameType: "minecraft",
            IsPublic: false,
            ProjectUrl: null,
            IconUrl: null,
            Tags: null);

        var result = _createValidator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.Slug);
    }

    [Theory]
    [InlineData("-mod")]
    [InlineData("MOD")]
    [InlineData("mod-")]
    [InlineData("My Mod")]
    public void CreateMod_InvalidSlugs_FailValidation(string slug)
    {
        var request = new CreateModRequest(
            Name: "My Mod",
            Slug: slug,
            Description: null,
            Author: null,
            CategoryId: null,
            GameType: "minecraft",
            IsPublic: false,
            ProjectUrl: null,
            IconUrl: null,
            Tags: null);

        var result = _createValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Slug);
    }

    [Fact]
    public void CreateMod_EmptyGameType_FailsValidation()
    {
        var request = new CreateModRequest(
            Name: "My Mod",
            Slug: "my-mod",
            Description: null,
            Author: null,
            CategoryId: null,
            GameType: "",
            IsPublic: false,
            ProjectUrl: null,
            IconUrl: null,
            Tags: null);

        var result = _createValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.GameType);
    }

    [Theory]
    [InlineData("ftp://example.com/icon.png")]
    [InlineData("not-a-url")]
#pragma warning disable CA1054
    public void CreateMod_InvalidUrlScheme_FailsValidation(string url)
#pragma warning restore CA1054
    {
        var request = new CreateModRequest(
            Name: "My Mod",
            Slug: "my-mod",
            Description: null,
            Author: null,
            CategoryId: null,
            GameType: "minecraft",
            IsPublic: false,
            ProjectUrl: url,
            IconUrl: null,
            Tags: null);

        var result = _createValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ProjectUrl);
    }

    [Fact]
    public void CreateMod_TooManyTags_FailsValidation()
    {
        var tags = Enumerable.Range(0, 21).Select(i => $"tag{i}").ToList();
        var request = new CreateModRequest(
            Name: "My Mod",
            Slug: "my-mod",
            Description: null,
            Author: null,
            CategoryId: null,
            GameType: "minecraft",
            IsPublic: false,
            ProjectUrl: null,
            IconUrl: null,
            Tags: tags);

        var result = _createValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Tags);
    }

    [Fact]
    public void CreateMod_TagExceedsMaxLength_FailsValidation()
    {
        var tags = new List<string> { new string('a', 51) };
        var request = new CreateModRequest(
            Name: "My Mod",
            Slug: "my-mod",
            Description: null,
            Author: null,
            CategoryId: null,
            GameType: "minecraft",
            IsPublic: false,
            ProjectUrl: null,
            IconUrl: null,
            Tags: tags);

        var result = _createValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor("Tags[0]");
    }

    // ── UpdateModRequestValidator ──────────────────────────────────────────

    private readonly UpdateModRequestValidator _updateValidator = new();

    [Fact]
    public void UpdateMod_AllNullFields_PassesValidation()
    {
        var request = new UpdateModRequest(
            Name: null,
            Description: null,
            Author: null,
            CategoryId: null,
            IsPublic: null,
            IsArchived: null,
            ProjectUrl: null,
            IconUrl: null,
            Tags: null);

        var result = _updateValidator.TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateMod_NameExceedsMaxLength_FailsValidation()
    {
        var request = new UpdateModRequest(
            Name: new string('a', 101),
            Description: null,
            Author: null,
            CategoryId: null,
            IsPublic: null,
            IsArchived: null,
            ProjectUrl: null,
            IconUrl: null,
            Tags: null);

        var result = _updateValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void UpdateMod_InvalidIconUrl_FailsValidation()
    {
        var request = new UpdateModRequest(
            Name: null,
            Description: null,
            Author: null,
            CategoryId: null,
            IsPublic: null,
            IsArchived: null,
            ProjectUrl: null,
            IconUrl: "ftp://bad-scheme.com/icon.png",
            Tags: null);

        var result = _updateValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.IconUrl);
    }

    // ── PublishVersionRequestValidator ──────────────────────────────────────

    private readonly PublishVersionRequestValidator _publishValidator = new();

    [Fact]
    public void PublishVersion_ValidRequest_PassesValidation()
    {
        var request = new PublishVersionRequest(
            Version: "1.2.3",
            ReleaseNotes: "Initial release",
            FileHash: "abc123",
            FileSizeBytes: 1024,
            FilePath: "/mods/test.zip",
            MinGameVersion: "1.0.0",
            MaxGameVersion: "2.0.0",
            IsPrerelease: false,
            Dependencies: null);

        var result = _publishValidator.TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void PublishVersion_EmptyVersion_FailsValidation()
    {
        var request = new PublishVersionRequest(
            Version: "",
            ReleaseNotes: null,
            FileHash: null,
            FileSizeBytes: 1024,
            FilePath: null,
            MinGameVersion: null,
            MaxGameVersion: null,
            IsPrerelease: false,
            Dependencies: null);

        var result = _publishValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Version);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("0.0.1")]
    [InlineData("1.0.0-beta.1")]
    [InlineData("2.1.0-alpha")]
    [InlineData("1.0.0+build.123")]
    [InlineData("1.0.0-rc.1+build.456")]
    public void PublishVersion_ValidVersionFormats_PassValidation(string version)
    {
        var request = new PublishVersionRequest(
            Version: version,
            ReleaseNotes: null,
            FileHash: null,
            FileSizeBytes: 100,
            FilePath: null,
            MinGameVersion: null,
            MaxGameVersion: null,
            IsPrerelease: false,
            Dependencies: null);

        var result = _publishValidator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.Version);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("1.2")]
    [InlineData("v1.2.3")]
    public void PublishVersion_InvalidVersionFormats_FailValidation(string version)
    {
        var request = new PublishVersionRequest(
            Version: version,
            ReleaseNotes: null,
            FileHash: null,
            FileSizeBytes: 100,
            FilePath: null,
            MinGameVersion: null,
            MaxGameVersion: null,
            IsPrerelease: false,
            Dependencies: null);

        var result = _publishValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PublishVersion_NonPositiveFileSize_FailsValidation(long fileSize)
    {
        var request = new PublishVersionRequest(
            Version: "1.0.0",
            ReleaseNotes: null,
            FileHash: null,
            FileSizeBytes: fileSize,
            FilePath: null,
            MinGameVersion: null,
            MaxGameVersion: null,
            IsPrerelease: false,
            Dependencies: null);

        var result = _publishValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.FileSizeBytes);
    }

    // ── DeprecateVersionRequestValidator ───────────────────────────────────

    private readonly DeprecateVersionRequestValidator _deprecateValidator = new();

    [Fact]
    public void DeprecateVersion_ValidRequest_PassesValidation()
    {
        var request = new DeprecateVersionRequest(
            Reason: "Security vulnerability found",
            RecommendedVersionId: Guid.NewGuid());

        var result = _deprecateValidator.TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void DeprecateVersion_EmptyReason_FailsValidation()
    {
        var request = new DeprecateVersionRequest(
            Reason: "",
            RecommendedVersionId: null);

        var result = _deprecateValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void DeprecateVersion_ReasonExceedsMaxLength_FailsValidation()
    {
        var request = new DeprecateVersionRequest(
            Reason: new string('a', 1001),
            RecommendedVersionId: null);

        var result = _deprecateValidator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }
}
