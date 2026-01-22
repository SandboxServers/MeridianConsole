using System.Threading.Channels;
using Dhadgar.ServiceDefaults.Audit;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.Audit;

/// <summary>
/// Tests for <see cref="AuditQueue"/> channel-based queue behavior.
/// </summary>
public class AuditQueueTests
{
    private static IOptions<AuditQueueOptions> CreateOptions(int capacity = 100)
    {
        return Options.Create(new AuditQueueOptions { Capacity = capacity });
    }

    [Fact]
    public async Task QueueAsync_AddsRecordToChannel()
    {
        // Arrange
        var queue = new AuditQueue(CreateOptions());
        var record = new ApiAuditRecord
        {
            HttpMethod = "GET",
            Path = "/test",
            StatusCode = 200,
            TimestampUtc = DateTime.UtcNow
        };

        // Act
        await queue.QueueAsync(record);

        // Assert - read it back
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await foreach (var r in queue.ReadAllAsync(cts.Token))
        {
            r.Path.Should().Be("/test");
            r.HttpMethod.Should().Be("GET");
            break;
        }
    }

    [Fact]
    public async Task ReadAllAsync_ReturnsQueuedRecordsInOrder()
    {
        // Arrange
        var queue = new AuditQueue(CreateOptions());
        var records = new[]
        {
            new ApiAuditRecord { HttpMethod = "GET", Path = "/first", StatusCode = 200 },
            new ApiAuditRecord { HttpMethod = "POST", Path = "/second", StatusCode = 201 },
            new ApiAuditRecord { HttpMethod = "PUT", Path = "/third", StatusCode = 200 }
        };

        // Act
        foreach (var record in records)
        {
            await queue.QueueAsync(record);
        }

        // Assert - read them back in order
        var readRecords = new List<ApiAuditRecord>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await foreach (var r in queue.ReadAllAsync(cts.Token))
        {
            readRecords.Add(r);
            if (readRecords.Count >= 3)
            {
                break;
            }
        }

        readRecords.Should().HaveCount(3);
        readRecords[0].Path.Should().Be("/first");
        readRecords[1].Path.Should().Be("/second");
        readRecords[2].Path.Should().Be("/third");
    }

    [Fact]
    public async Task QueueAsync_MultipleWritersSingleReader_MaintainsAllRecords()
    {
        // Arrange
        var queue = new AuditQueue(CreateOptions(capacity: 1000));
        const int recordCount = 100;
        const int writerCount = 5;

        // Act - multiple writers adding records concurrently
        var writeTasks = Enumerable.Range(0, writerCount)
            .Select(async writerId =>
            {
                for (var i = 0; i < recordCount / writerCount; i++)
                {
                    await queue.QueueAsync(new ApiAuditRecord
                    {
                        HttpMethod = "GET",
                        Path = $"/writer{writerId}/record{i}",
                        StatusCode = 200
                    });
                }
            });

        await Task.WhenAll(writeTasks);

        // Assert - read all records
        var readRecords = new List<ApiAuditRecord>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var r in queue.ReadAllAsync(cts.Token))
        {
            readRecords.Add(r);
            if (readRecords.Count >= recordCount)
            {
                break;
            }
        }

        readRecords.Should().HaveCount(recordCount);
    }

    [Fact]
    public void Complete_PreventsAdditionalWrites()
    {
        // Arrange
        var queue = new AuditQueue(CreateOptions());

        // Act
        queue.Complete();

        // Assert - QueueAsync should throw after completion
        var act = async () => await queue.QueueAsync(new ApiAuditRecord
        {
            HttpMethod = "GET",
            Path = "/test",
            StatusCode = 200
        });

        act.Should().ThrowAsync<ChannelClosedException>();
    }

    [Fact]
    public async Task Complete_AllowsDrainingRemainingRecords()
    {
        // Arrange
        var queue = new AuditQueue(CreateOptions());
        await queue.QueueAsync(new ApiAuditRecord
        {
            HttpMethod = "GET",
            Path = "/remaining",
            StatusCode = 200
        });

        // Act
        queue.Complete();

        // Assert - can still read remaining records
        var readRecords = new List<ApiAuditRecord>();
        await foreach (var r in queue.ReadAllAsync(CancellationToken.None))
        {
            readRecords.Add(r);
        }

        readRecords.Should().HaveCount(1);
        readRecords[0].Path.Should().Be("/remaining");
    }
}
