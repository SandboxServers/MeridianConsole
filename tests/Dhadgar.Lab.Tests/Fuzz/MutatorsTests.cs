using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Fuzz;
using Dhadgar.Lab.Http;

namespace Dhadgar.Lab.Tests.Fuzz;

public sealed class MutatorsTests
{
    private const ulong Seed = 20261028UL;

    [Fact]
    public void Mutators_SameSeed_SameCase()
    {
        // Reverting the seeded stream (a shared Random, or a Guid) makes this fail: the same index must give the same bytes.
        for (int index = 0; index < 60; index++)
        {
            FuzzCase first = Mutators.Build(FuzzTargets.Enroll, Seed, index);
            FuzzCase second = Mutators.Build(FuzzTargets.Enroll, Seed, index);

            Assert.Equal(first.Mutation, second.Mutation);
            Assert.Equal(first.ContentType, second.ContentType);
            Assert.True(first.Body.Span.SequenceEqual(second.Body.Span));
        }
    }

    [Fact]
    public void Mutators_OneCase_ReplaysAlone()
    {
        FuzzCase full = Enumerable.Range(0, 80).Select(i => Mutators.Build(FuzzTargets.Enroll, Seed, i)).Last();
        FuzzCase alone = Mutators.Build(FuzzTargets.Enroll, Seed, 79);

        Assert.True(full.Body.Span.SequenceEqual(alone.Body.Span));
        Assert.Equal(full.Mutation, alone.Mutation);
    }

    [Fact]
    public void Mutators_DifferentSeed_DifferentCases()
    {
        int differing = 0;
        for (int index = 0; index < 60; index++)
        {
            ReadOnlyMemory<byte> first = Mutators.Build(FuzzTargets.Enroll, Seed, index).Body;
            ReadOnlyMemory<byte> second = Mutators.Build(FuzzTargets.Enroll, Seed + 1, index).Body;
            if (!first.Span.SequenceEqual(second.Span))
            {
                differing++;
            }
        }

        Assert.True(differing > 30, "a different seed must change most cases");
    }

    [Fact]
    public void Mutators_AllKindsAndRequiredBoundariesReachable()
    {
        var mutations = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < 1200; index++)
        {
            mutations.Add(Mutators.Build(FuzzTargets.Enroll, Seed, index).Mutation);
        }

        foreach (string kind in Mutators.Kinds.Where(k => k != "boundary"))
        {
            Assert.Contains(kind, mutations);
        }

        // The 64 KiB and 1 MiB boundary strings are required on enroll (L28 amendment).
        Assert.Contains("boundary:64KiB", mutations);
        Assert.Contains("boundary:1MiB-minus-envelope", mutations);
    }

    [Fact]
    public void Mutators_OversizeBoundary_FitsTheRequestLimit()
    {
        // LabHttp refuses bodies over 1 MiB, so the 1 MiB boundary must reach the Gateway as a real request.
        foreach ((string label, string value) in Mutators.BoundaryStrings)
        {
            byte[] body = Encoding.UTF8.GetBytes("{\"token\":\"" + value + "\"}");
            Assert.True(body.Length <= LabHttp.MaxRequestBytes, label + " must fit the request limit");
        }
    }

    [Fact]
    public void Mutators_Truncated_IsNotJson()
    {
        for (int index = 0; index < 400; index++)
        {
            FuzzCase fuzzCase = Mutators.Build(FuzzTargets.Enroll, Seed, index);
            if (fuzzCase.Mutation != "truncated-json")
            {
                continue;
            }

            Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(fuzzCase.Body));
        }
    }

    [Fact]
    public void FuzzTargets_ValidBody_IsCamelCaseContractJson()
    {
        JsonObject root = (JsonObject)JsonNode.Parse(FuzzTargets.ValidBody(FuzzTargets.Enroll))!;

        Assert.NotNull(root["token"]);
        Assert.NotNull(root["publicKey"]);
        Assert.NotNull(root["hardware"]);
    }

    [Fact]
    public void FuzzTargets_UnknownName_IsNotFound()
    {
        var found = FuzzTargets.Find("no-such-target");

        Assert.True(found.IsFailure);
        Assert.Contains("[Lab.NotFound]", found.Error, StringComparison.Ordinal);
        Assert.True(FuzzTargets.Find("enroll").IsSuccess);
    }
}
