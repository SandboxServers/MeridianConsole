using Dhadgar.Lab.Random;

namespace Dhadgar.Lab.Tests.Random;

public sealed class LabRandomTests
{
    [Fact]
    public void NextUInt64_SeedFortyTwoRunnerStream_PinnedFirstFiveOutputs()
    {
        // Computed once from the C17 generator. Any change to the generator breaks replays and must fail here.
        ulong[] expected =
        [
            0x455BA75C6951959BUL,
            0x5FFD352CA0EADF55UL,
            0xD004CA4CEE08376BUL,
            0x0192EB0A04910D06UL,
            0x183ADEB46A31DE92UL,
        ];

        var random = new LabRandom(42, "runner");
        ulong[] actual = new ulong[expected.Length];
        for (int i = 0; i < actual.Length; i++)
        {
            actual[i] = random.NextUInt64();
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NextUInt64_SameSeedAndStream_SameSequence()
    {
        var first = new LabRandom(7, "fault:f1");
        var second = new LabRandom(7, "fault:f1");

        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(first.NextUInt64(), second.NextUInt64());
        }
    }

    [Fact]
    public void NextUInt64_DifferentStreams_DifferentSequences()
    {
        var runner = new LabRandom(42, "runner");
        var fault = new LabRandom(42, "fault:f1");

        Assert.NotEqual(runner.NextUInt64(), fault.NextUInt64());
    }

    [Fact]
    public void Next_Range_StaysWithinBounds()
    {
        var random = new LabRandom(1, "runner");

        for (int i = 0; i < 1000; i++)
        {
            int value = random.Next(-3, 4);
            Assert.InRange(value, -3, 3);
        }
    }

    [Fact]
    public void Next_EmptyRange_Throws()
    {
        var random = new LabRandom(1, "runner");

        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(5, 5));
    }

    [Fact]
    public void NextDouble_Values_AreBelowOne()
    {
        var random = new LabRandom(9, "runner");

        for (int i = 0; i < 1000; i++)
        {
            double value = random.NextDouble();
            Assert.InRange(value, 0.0, 0.9999999999999999);
        }
    }

    [Fact]
    public void NextBytes_SameSeed_SameBytes()
    {
        var first = new byte[13];
        var second = new byte[13];

        new LabRandom(5, "runner").NextBytes(first);
        new LabRandom(5, "runner").NextBytes(second);

        Assert.Equal(first, second);
    }

    [Fact]
    public void NewSeed_TwoCalls_AreNotBothZero()
    {
        ulong first = LabRandom.NewSeed();
        ulong second = LabRandom.NewSeed();

        Assert.True(first != 0 || second != 0);
    }
}
