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
    public void Run7Seed_First50Cases_AreAllSendable()
    {
        // Run 7 seed (F107): every enroll case of the failing job must fit LabHttp.MaxRequestBytes, or LabHttp refuses it
        // before any send and the whole fuzz job aborts.
        const ulong run7Seed = 14372532009034476853UL;
        for (int index = 0; index < 50; index++)
        {
            FuzzCase fuzzCase = Mutators.Build(FuzzTargets.Enroll, run7Seed, index);

            Assert.True(
                fuzzCase.Body.Length <= LabHttp.MaxRequestBytes,
                $"case {index} ({fuzzCase.Mutation}) is {fuzzCase.Body.Length} bytes, over {LabHttp.MaxRequestBytes}");
        }
    }

    [Fact]
    public void Boundary1MiB_FitsTheRequestLimit_ForEveryProperty()
    {
        // The 1 MiB boundary is sized per body (F107): each case must be exactly the request limit, whichever of the five
        // enroll properties it replaces. Reverting to a fixed 256-byte envelope makes this fail with 1 048 632 bytes and up.
        JsonObject valid = (JsonObject)JsonNode.Parse(FuzzTargets.ValidBody(FuzzTargets.Enroll))!;
        var replaced = new HashSet<string>(StringComparer.Ordinal);
        int oversize = 0;
        for (int index = 0; index < 20000; index++)
        {
            FuzzCase fuzzCase = Mutators.Build(FuzzTargets.Enroll, 1UL, index);
            if (fuzzCase.Mutation != "boundary:" + Mutators.OversizeLabel)
            {
                continue;
            }

            oversize++;
            Assert.Equal(LabHttp.MaxRequestBytes, fuzzCase.Body.Length);

            JsonObject body = (JsonObject)JsonNode.Parse(fuzzCase.Body.ToArray())!;
            foreach (KeyValuePair<string, JsonNode?> pair in valid)
            {
                if (!JsonNode.DeepEquals(pair.Value, body[pair.Key]))
                {
                    _ = replaced.Add(pair.Key);
                }
            }
        }

        Assert.True(oversize > 0, "the seed must draw the 1 MiB boundary at least once");
        Assert.Equal(valid.Count, replaced.Count);
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
