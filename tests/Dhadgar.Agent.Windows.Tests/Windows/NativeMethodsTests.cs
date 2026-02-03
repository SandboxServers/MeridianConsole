using System.Runtime.InteropServices;
using Dhadgar.Agent.Windows.Windows;
using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Windows;

/// <summary>
/// Tests for NativeMethods P/Invoke declarations.
/// These tests verify that enum values and struct layouts match Windows API expectations.
/// </summary>
public sealed class NativeMethodsTests
{
    #region Enum Value Tests

    [Theory]
    [InlineData(nameof(NativeMethods.JobObjectInfoType.BasicLimitInformation), 2)]
    [InlineData(nameof(NativeMethods.JobObjectInfoType.ExtendedLimitInformation), 9)]
    [InlineData(nameof(NativeMethods.JobObjectInfoType.CpuRateControlInformation), 15)]
    public void JobObjectInfoType_HasExpectedValue(string memberName, int expectedValue)
    {
        // Arrange & Act
        var value = Enum.Parse<NativeMethods.JobObjectInfoType>(memberName);

        // Assert
        Assert.Equal(expectedValue, (int)value);
    }

    [Theory]
    [InlineData(nameof(NativeMethods.JobObjectLimit.None), 0x00000000u)]
    [InlineData(nameof(NativeMethods.JobObjectLimit.ProcessMemory), 0x00000100u)]
    [InlineData(nameof(NativeMethods.JobObjectLimit.JobMemory), 0x00000200u)]
    [InlineData(nameof(NativeMethods.JobObjectLimit.KillOnJobClose), 0x00002000u)]
    public void JobObjectLimit_HasExpectedValue(string memberName, uint expectedValue)
    {
        // Arrange & Act
        var value = Enum.Parse<NativeMethods.JobObjectLimit>(memberName);

        // Assert
        Assert.Equal(expectedValue, (uint)value);
    }

    [Theory]
    [InlineData(nameof(NativeMethods.CpuRateControlFlags.None), 0x00000000u)]
    [InlineData(nameof(NativeMethods.CpuRateControlFlags.Enable), 0x00000001u)]
    [InlineData(nameof(NativeMethods.CpuRateControlFlags.HardCap), 0x00000004u)]
    [InlineData(nameof(NativeMethods.CpuRateControlFlags.WeightBased), 0x00000008u)]
    public void CpuRateControlFlags_HasExpectedValue(string memberName, uint expectedValue)
    {
        // Arrange & Act
        var value = Enum.Parse<NativeMethods.CpuRateControlFlags>(memberName);

        // Assert
        Assert.Equal(expectedValue, (uint)value);
    }

    #endregion

    #region Struct Size Tests

    [Fact]
    public void JOBOBJECT_BASIC_LIMIT_INFORMATION_HasExpectedSize()
    {
        // Arrange
        var expectedSize = Environment.Is64BitProcess
            ? 64  // 64-bit: 8 + 8 + 4 (+ 4 padding) + 8 + 8 + 4 (+ 4 padding) + 8 + 4 + 4 = 64 bytes
            : 48; // 32-bit: 8 + 8 + 4 + 4 + 4 + 4 + 4 + 4 + 4 = 48 bytes

        // Act
        var actualSize = Marshal.SizeOf<NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION>();

        // Assert
        Assert.Equal(expectedSize, actualSize);
    }

    [Fact]
    public void IO_COUNTERS_HasExpectedSize()
    {
        // Arrange
        const int expectedSize = 48; // 6 * ulong (8 bytes each) = 48 bytes

        // Act
        var actualSize = Marshal.SizeOf<NativeMethods.IO_COUNTERS>();

        // Assert
        Assert.Equal(expectedSize, actualSize);
    }

    [Fact]
    public void JOBOBJECT_EXTENDED_LIMIT_INFORMATION_HasExpectedSize()
    {
        // Arrange
        var expectedSize = Environment.Is64BitProcess
            ? 144 // 64-bit: JOBOBJECT_BASIC_LIMIT_INFORMATION (64) + IO_COUNTERS (48) + 4 * nuint (8 bytes each) = 144 bytes
            : 104; // 32-bit: JOBOBJECT_BASIC_LIMIT_INFORMATION (48) + IO_COUNTERS (48) + 4 * nuint (4 bytes each) = 104 bytes

        // Act
        var actualSize = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();

        // Assert
        Assert.Equal(expectedSize, actualSize);
    }

    [Fact]
    public void JOBOBJECT_CPU_RATE_CONTROL_INFORMATION_HasExpectedSize()
    {
        // Arrange
        // Win32 union: ControlFlags (4 bytes) + union of CpuRate/Weight/MinRate+MaxRate (4 bytes) = 8 bytes
        // The union members share the same 4 bytes at offset 4
        const int expectedSize = 8;

        // Act
        var actualSize = Marshal.SizeOf<NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>();

        // Assert
        Assert.Equal(expectedSize, actualSize);
    }

    #endregion

    #region Struct Field Layout Tests

    [Fact]
    public void JOBOBJECT_BASIC_LIMIT_INFORMATION_FieldOffsets_AreCorrect()
    {
        // This test verifies that the struct layout is sequential and fields are in the expected order
        // by checking the offset of the first and last fields.

        // Arrange
        var type = typeof(NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION);

        // Act
        var perProcessUserTimeLimitOffset = Marshal.OffsetOf(type, nameof(NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION.PerProcessUserTimeLimit));
        var schedulingClassOffset = Marshal.OffsetOf(type, nameof(NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION.SchedulingClass));

        // Assert
        Assert.Equal(0, perProcessUserTimeLimitOffset.ToInt32()); // First field should be at offset 0
        Assert.True(schedulingClassOffset.ToInt32() > 0); // Last field should be at a positive offset
    }

    [Fact]
    public void IO_COUNTERS_FieldOffsets_AreCorrect()
    {
        // Arrange
        var type = typeof(NativeMethods.IO_COUNTERS);

        // Act
        var readOperationCountOffset = Marshal.OffsetOf(type, nameof(NativeMethods.IO_COUNTERS.ReadOperationCount));
        var otherTransferCountOffset = Marshal.OffsetOf(type, nameof(NativeMethods.IO_COUNTERS.OtherTransferCount));

        // Assert
        Assert.Equal(0, readOperationCountOffset.ToInt32()); // First field should be at offset 0
        Assert.Equal(40, otherTransferCountOffset.ToInt32()); // Last field at offset 40 (5 * 8 bytes)
    }

    [Fact]
    public void JOBOBJECT_EXTENDED_LIMIT_INFORMATION_FieldOffsets_AreCorrect()
    {
        // Arrange
        var type = typeof(NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION);

        // Act
        var basicLimitInformationOffset = Marshal.OffsetOf(type, nameof(NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION.BasicLimitInformation));
        var ioInfoOffset = Marshal.OffsetOf(type, nameof(NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION.IoInfo));

        // Assert
        Assert.Equal(0, basicLimitInformationOffset.ToInt32()); // First field should be at offset 0

        var expectedIoInfoOffset = Environment.Is64BitProcess ? 64 : 48;
        Assert.Equal(expectedIoInfoOffset, ioInfoOffset.ToInt32()); // IoInfo follows BasicLimitInformation
    }

    [Fact]
    public void JOBOBJECT_CPU_RATE_CONTROL_INFORMATION_FieldOffsets_AreCorrect()
    {
        // Arrange
        var type = typeof(NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION);

        // Act
        var controlFlagsOffset = Marshal.OffsetOf(type, nameof(NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION.ControlFlags));
        var cpuRateOffset = Marshal.OffsetOf(type, nameof(NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION.CpuRate));
        var maxRateOffset = Marshal.OffsetOf(type, nameof(NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION.MaxRate));

        // Assert
        Assert.Equal(0, controlFlagsOffset.ToInt32()); // ControlFlags at offset 0
        Assert.Equal(4, cpuRateOffset.ToInt32());      // Union starts at offset 4
        Assert.Equal(6, maxRateOffset.ToInt32());      // MaxRate is second ushort in MinRate/MaxRate struct (offset 4 + 2 = 6)
    }

    #endregion

    #region Union Field Tests (MinRate/Weight share CpuRate field)

    [Fact]
    public void JOBOBJECT_CPU_RATE_CONTROL_INFORMATION_MinRate_SharesCpuRateStorage()
    {
        // Arrange
        var info = new NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            CpuRate = 5000
        };

        // Act
        var minRate = info.MinRate;

        // Assert
        Assert.Equal(5000u, minRate);
    }

    [Fact]
    public void JOBOBJECT_CPU_RATE_CONTROL_INFORMATION_Weight_SharesCpuRateStorage()
    {
        // Arrange
        var info = new NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            CpuRate = 7
        };

        // Act
        var weight = info.Weight;

        // Assert
        Assert.Equal(7u, weight);
    }

    [Fact]
    public void JOBOBJECT_CPU_RATE_CONTROL_INFORMATION_MinRateSetter_UpdatesCpuRate()
    {
        // Arrange
        var info = new NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION();

        // Act
        info.MinRate = 3000;

        // Assert
        Assert.Equal(3000u, info.CpuRate);
    }

    [Fact]
    public void JOBOBJECT_CPU_RATE_CONTROL_INFORMATION_WeightSetter_UpdatesCpuRate()
    {
        // Arrange
        var info = new NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION();

        // Act
        info.Weight = 5;

        // Assert
        Assert.Equal(5u, info.CpuRate);
    }

    #endregion

    #region Flags Enum Combination Tests

    [Fact]
    public void JobObjectLimit_FlagsCanBeCombined()
    {
        // Arrange & Act
        var combined = NativeMethods.JobObjectLimit.KillOnJobClose |
                      NativeMethods.JobObjectLimit.JobMemory;

        // Assert
        Assert.Equal(0x00002200u, (uint)combined);
        Assert.True(combined.HasFlag(NativeMethods.JobObjectLimit.KillOnJobClose));
        Assert.True(combined.HasFlag(NativeMethods.JobObjectLimit.JobMemory));
    }

    [Fact]
    public void CpuRateControlFlags_FlagsCanBeCombined()
    {
        // Arrange & Act
        var combined = NativeMethods.CpuRateControlFlags.Enable |
                      NativeMethods.CpuRateControlFlags.HardCap;

        // Assert
        Assert.Equal(0x00000005u, (uint)combined);
        Assert.True(combined.HasFlag(NativeMethods.CpuRateControlFlags.Enable));
        Assert.True(combined.HasFlag(NativeMethods.CpuRateControlFlags.HardCap));
    }

    #endregion
}
