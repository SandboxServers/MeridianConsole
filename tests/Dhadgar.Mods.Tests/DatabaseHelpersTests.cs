using Dhadgar.Shared.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dhadgar.Mods.Tests;

public class DatabaseHelpersTests
{
    // ── IsUniqueConstraintViolation ────────────────────────────────────────

    [Fact]
    public void IsUniqueConstraintViolation_DuplicateKeyMessage_ReturnsTrue()
    {
        var inner = new Exception("ERROR: duplicate key value violates unique constraint");
        var ex = new DbUpdateException("Save failed", inner);

        var result = DatabaseHelpers.IsUniqueConstraintViolation(ex);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsUniqueConstraintViolation_UniqueConstraintMessage_ReturnsTrue()
    {
        var inner = new Exception("UNIQUE constraint failed: Mods.Slug");
        var ex = new DbUpdateException("Save failed", inner);

        var result = DatabaseHelpers.IsUniqueConstraintViolation(ex);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsUniqueConstraintViolation_OtherError_ReturnsFalse()
    {
        var inner = new Exception("Some other database error occurred");
        var ex = new DbUpdateException("Save failed", inner);

        var result = DatabaseHelpers.IsUniqueConstraintViolation(ex);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsUniqueConstraintViolation_NoInnerException_ReturnsFalse()
    {
        var ex = new DbUpdateException("Save failed");

        var result = DatabaseHelpers.IsUniqueConstraintViolation(ex);

        result.Should().BeFalse();
    }

    // ── EscapeLikePattern ──────────────────────────────────────────────────

    [Fact]
    public void EscapeLikePattern_EscapesPercent()
    {
        var result = DatabaseHelpers.EscapeLikePattern("100%");

        result.Should().Be("100\\%");
    }

    [Fact]
    public void EscapeLikePattern_EscapesUnderscore()
    {
        var result = DatabaseHelpers.EscapeLikePattern("my_mod");

        result.Should().Be("my\\_mod");
    }

    [Fact]
    public void EscapeLikePattern_EscapesBackslash()
    {
        var result = DatabaseHelpers.EscapeLikePattern("path\\to\\mod");

        result.Should().Be("path\\\\to\\\\mod");
    }

    [Fact]
    public void EscapeLikePattern_NoSpecialChars_ReturnsUnchanged()
    {
        var result = DatabaseHelpers.EscapeLikePattern("normal text");

        result.Should().Be("normal text");
    }
}
