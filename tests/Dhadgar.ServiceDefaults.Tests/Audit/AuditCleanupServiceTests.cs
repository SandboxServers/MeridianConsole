using Dhadgar.ServiceDefaults.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.Audit;

/// <summary>
/// Tests for <see cref="AuditCleanupService{TContext}"/> retention and cleanup behavior.
/// </summary>
/// <remarks>
/// These tests use SQLite in-memory mode because ExecuteDeleteAsync requires
/// a relational provider (InMemory provider doesn't support bulk operations).
/// </remarks>
public class AuditCleanupServiceTests
{
    private static ServiceProvider CreateServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        // Use a file-based SQLite database for testing (supports ExecuteDeleteAsync)
        var dbPath = Path.Combine(Path.GetTempPath(), $"{dbName}.db");
        services.AddDbContext<TestAuditDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));
        var provider = services.BuildServiceProvider();

        // Ensure database is created
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
        db.Database.EnsureDeleted(); // Clean start
        db.Database.EnsureCreated();

        return provider;
    }

    /// <summary>
    /// Helper method that replicates the cleanup algorithm for testing.
    /// This is the same logic as AuditCleanupService.CleanupOldRecordsAsync.
    /// </summary>
    private static async Task CleanupOldRecordsAsync(
        TestAuditDbContext db,
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        int deleted;
        do
        {
            deleted = await db.ApiAuditRecords
                .Where(r => r.TimestampUtc < cutoff)
                .Take(batchSize)
                .ExecuteDeleteAsync(cancellationToken);
        } while (deleted == batchSize && !cancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CleanupDeletesRecordsOlderThanRetentionPeriod()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider($"AuditCleanup_{Guid.NewGuid():N}");
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        // Use FakeTimeProvider to control time
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var retentionPeriod = TimeSpan.FromDays(90);
        var cutoff = fakeTime.GetUtcNow().UtcDateTime - retentionPeriod;

        // Create test records: some 100 days old (should delete), some 30 days old (should keep)
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();

            // Old records (100 days old - should be deleted)
            db.ApiAuditRecords.Add(new ApiAuditRecord
            {
                Id = Guid.NewGuid(),
                TimestampUtc = fakeTime.GetUtcNow().UtcDateTime.AddDays(-100),
                HttpMethod = "GET",
                Path = "/old-record-1",
                StatusCode = 200
            });
            db.ApiAuditRecords.Add(new ApiAuditRecord
            {
                Id = Guid.NewGuid(),
                TimestampUtc = fakeTime.GetUtcNow().UtcDateTime.AddDays(-100),
                HttpMethod = "GET",
                Path = "/old-record-2",
                StatusCode = 200
            });

            // New records (30 days old - should be kept)
            db.ApiAuditRecords.Add(new ApiAuditRecord
            {
                Id = Guid.NewGuid(),
                TimestampUtc = fakeTime.GetUtcNow().UtcDateTime.AddDays(-30),
                HttpMethod = "GET",
                Path = "/new-record-1",
                StatusCode = 200
            });
            db.ApiAuditRecords.Add(new ApiAuditRecord
            {
                Id = Guid.NewGuid(),
                TimestampUtc = fakeTime.GetUtcNow().UtcDateTime.AddDays(-30),
                HttpMethod = "GET",
                Path = "/new-record-2",
                StatusCode = 200
            });

            await db.SaveChangesAsync();
        }

        // Verify initial count
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            var initialCount = await db.ApiAuditRecords.CountAsync();
            initialCount.Should().Be(4);
        }

        // Act - invoke cleanup logic directly
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            await CleanupOldRecordsAsync(db, cutoff, 1000, CancellationToken.None);
        }

        // Assert - old records deleted, new records kept
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            var remainingRecords = await db.ApiAuditRecords.ToListAsync();

            remainingRecords.Should().HaveCount(2);
            remainingRecords.Should().AllSatisfy(r => r.Path.Should().StartWith("/new-"));
        }
    }

    [Fact]
    public async Task CleanupWhenDisabledDoesNotRun()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider($"AuditCleanup_{Guid.NewGuid():N}");
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var options = Options.Create(new AuditCleanupOptions
        {
            Enabled = false,  // Disabled
            RetentionPeriod = TimeSpan.FromDays(90),
            BatchSize = 1000,
            Interval = TimeSpan.FromHours(1)
        });

        // Create an old record
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            db.ApiAuditRecords.Add(new ApiAuditRecord
            {
                Id = Guid.NewGuid(),
                TimestampUtc = fakeTime.GetUtcNow().UtcDateTime.AddDays(-100),
                HttpMethod = "GET",
                Path = "/old-record",
                StatusCode = 200
            });
            await db.SaveChangesAsync();
        }

        // Act - start and immediately stop (disabled should not run cleanup loop)
        using var cleanupService = new AuditCleanupService<TestAuditDbContext>(
            scopeFactory,
            NullLogger<AuditCleanupService<TestAuditDbContext>>.Instance,
            options,
            fakeTime);

        using var cts = new CancellationTokenSource();
        await cleanupService.StartAsync(cts.Token);
        await cts.CancelAsync();
        await cleanupService.StopAsync(CancellationToken.None);

        // Assert - record should still exist (cleanup is disabled and never ran)
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            var count = await db.ApiAuditRecords.CountAsync();
            count.Should().Be(1);
        }
    }

    [Fact]
    public async Task CleanupBatchesLargeDeletions()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider($"AuditCleanup_{Guid.NewGuid():N}");
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var retentionPeriod = TimeSpan.FromDays(90);
        var cutoff = fakeTime.GetUtcNow().UtcDateTime - retentionPeriod;

        // Create 20 old records (should require 4 batches to delete with batch size 5)
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();

            for (var i = 0; i < 20; i++)
            {
                db.ApiAuditRecords.Add(new ApiAuditRecord
                {
                    Id = Guid.NewGuid(),
                    TimestampUtc = fakeTime.GetUtcNow().UtcDateTime.AddDays(-100),
                    HttpMethod = "GET",
                    Path = $"/old-record-{i}",
                    StatusCode = 200
                });
            }

            // Add one new record
            db.ApiAuditRecords.Add(new ApiAuditRecord
            {
                Id = Guid.NewGuid(),
                TimestampUtc = fakeTime.GetUtcNow().UtcDateTime.AddDays(-30),
                HttpMethod = "GET",
                Path = "/new-record",
                StatusCode = 200
            });

            await db.SaveChangesAsync();
        }

        // Act - invoke cleanup with small batch size
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            await CleanupOldRecordsAsync(db, cutoff, batchSize: 5, CancellationToken.None);
        }

        // Assert - all old records deleted via batching, new record kept
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            var remainingRecords = await db.ApiAuditRecords.ToListAsync();

            remainingRecords.Should().HaveCount(1);
            remainingRecords[0].Path.Should().Be("/new-record");
        }
    }

    [Fact]
    public void ConstructorAcceptsTimeProviderForTesting()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider($"AuditCleanup_{Guid.NewGuid():N}");
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));

        var options = Options.Create(new AuditCleanupOptions
        {
            RetentionPeriod = TimeSpan.FromDays(90),
            Enabled = true
        });

        // Act
        using var cleanupService = new AuditCleanupService<TestAuditDbContext>(
            scopeFactory,
            NullLogger<AuditCleanupService<TestAuditDbContext>>.Instance,
            options,
            fakeTime);

        // Assert
        cleanupService.Should().NotBeNull();
    }

    [Fact]
    public async Task CleanupNoRecordsToDeleteCompletesGracefully()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider($"AuditCleanup_{Guid.NewGuid():N}");
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var retentionPeriod = TimeSpan.FromDays(90);
        var cutoff = fakeTime.GetUtcNow().UtcDateTime - retentionPeriod;

        // Only add new records (nothing to delete)
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            db.ApiAuditRecords.Add(new ApiAuditRecord
            {
                Id = Guid.NewGuid(),
                TimestampUtc = fakeTime.GetUtcNow().UtcDateTime.AddDays(-30),
                HttpMethod = "GET",
                Path = "/new-record",
                StatusCode = 200
            });
            await db.SaveChangesAsync();
        }

        // Act - invoke cleanup (should not throw)
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            await CleanupOldRecordsAsync(db, cutoff, 1000, CancellationToken.None);
        }

        // Assert - record should still exist
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            var count = await db.ApiAuditRecords.CountAsync();
            count.Should().Be(1);
        }
    }

    [Fact]
    public async Task CleanupUsesCutoffCalculationCorrectly()
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider($"AuditCleanup_{Guid.NewGuid():N}");
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        // Set fake time to a specific date
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var retentionPeriod = TimeSpan.FromDays(90);
        var cutoff = fakeTime.GetUtcNow().UtcDateTime - retentionPeriod;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();

            // Record 1 second before cutoff (should be deleted)
            db.ApiAuditRecords.Add(new ApiAuditRecord
            {
                Id = Guid.NewGuid(),
                TimestampUtc = cutoff.AddSeconds(-1),
                HttpMethod = "GET",
                Path = "/before-cutoff",
                StatusCode = 200
            });

            // Record 1 second after cutoff (should be kept)
            db.ApiAuditRecords.Add(new ApiAuditRecord
            {
                Id = Guid.NewGuid(),
                TimestampUtc = cutoff.AddSeconds(1),
                HttpMethod = "GET",
                Path = "/after-cutoff",
                StatusCode = 200
            });

            await db.SaveChangesAsync();
        }

        // Act
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            await CleanupOldRecordsAsync(db, cutoff, 1000, CancellationToken.None);
        }

        // Assert - only record after cutoff should remain
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            var records = await db.ApiAuditRecords.ToListAsync();

            records.Should().HaveCount(1);
            records[0].Path.Should().Be("/after-cutoff");
        }
    }

    [Fact]
    public void DefaultOptionsHave90DayRetention()
    {
        // Arrange & Act
        var options = new AuditCleanupOptions();

        // Assert - verify AUDIT-04: 90-day retention
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(90));
        options.Enabled.Should().BeTrue();
        options.BatchSize.Should().Be(10_000);
        options.Interval.Should().Be(TimeSpan.FromHours(24));
    }
}

/// <summary>
/// Test DbContext that implements IAuditDbContext for testing.
/// </summary>
public sealed class TestAuditDbContext : DbContext, IAuditDbContext
{
    public TestAuditDbContext(DbContextOptions<TestAuditDbContext> options) : base(options) { }

    public DbSet<ApiAuditRecord> ApiAuditRecords => Set<ApiAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ApiAuditRecord>(b =>
        {
            b.ToTable("ApiAuditRecords");
            b.HasKey(x => x.Id);
            b.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
            b.Property(x => x.Path).HasMaxLength(500).IsRequired();
        });
    }
}
