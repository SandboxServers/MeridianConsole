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

    [Fact]
    public void JobObjectInfoType_ExtendedLimitInformation_EqualsNine()
    {
        // Arrange & Act
        var value = NativeMethods.JobObjectInfoType.ExtendedLimitInformation;

        // Assert
        Assert.Equal(9, (int)value);
    }

    [Fact]
    public void JobObjectInfoType_CpuRateControlInformation_EqualsFifteen()
    {
        // Arrange & Act
        var value = NativeMethods.JobObjectInfoType.CpuRateControlInformation;

        // Assert
        Assert.Equal(15, (int)value);
    }

    [Fact]
    public void JobObjectLimit_KillOnJobClose_Equals0x00002000()
    {
        // Arrange & Act
        var value = NativeMethods.JobObjectLimit.KillOnJobClose;

        // Assert
        Assert.Equal(0x00002000u, (uint)value);
    }

    [Fact]
    public void JobObjectLimit_JobMemory_Equals0x00000200()
    {
        // Arrange & Act
        var value = NativeMethods.JobObjectLimit.JobMemory;

        // Assert
        Assert.Equal(0x00000200u, (uint)value);
    }

    [Fact]
    public void CpuRateControlFlags_Enable_Equals0x00000001()
    {
        // Arrange & Act
        var value = NativeMethods.CpuRateControlFlags.Enable;

        // Assert
        Assert.Equal(0x00000001u, (uint)value);
    }

    [Fact]
    public void CpuRateControlFlags_HardCap_Equals0x00000004()
    {
        // Arrange & Act
        var value = NativeMethods.CpuRateControlFlags.HardCap;

        // Assert
        Assert.Equal(0x00000004u, (uint)value);
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
        const int expectedSize = 12; // CpuRateControlFlags (4) + CpuRate (4) + MaxRate (4) = 12 bytes

        // Act
        var actualSize = Marshal.SizeOf<NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>();

        // Assert
        Assert.Equal(expectedSize, actualSize);
    }

    #endregion

    #region Additional Enum Value Tests (for completeness)

    [Fact]
    public void JobObjectInfoType_BasicLimitInformation_EqualsTwo()
    {
        // Arrange & Act
        var value = NativeMethods.JobObjectInfoType.BasicLimitInformation;

        // Assert
        Assert.Equal(2, (int)value);
    }

    [Fact]
    public void JobObjectLimit_None_EqualsZero()
    {
        // Arrange & Act
        var value = NativeMethods.JobObjectLimit.None;

        // Assert
        Assert.Equal(0u, (uint)value);
    }

    [Fact]
    public void JobObjectLimit_ProcessMemory_Equals0x00000100()
    {
        // Arrange & Act
        var value = NativeMethods.JobObjectLimit.ProcessMemory;

        // Assert
        Assert.Equal(0x00000100u, (uint)value);
    }

    [Fact]
    public void CpuRateControlFlags_None_EqualsZero()
    {
        // Arrange & Act
        var value = NativeMethods.CpuRateControlFlags.None;

        // Assert
        Assert.Equal(0u, (uint)value);
    }

    [Fact]
    public void CpuRateControlFlags_WeightBased_Equals0x00000008()
    {
        // Arrange & Act
        var value = NativeMethods.CpuRateControlFlags.WeightBased;

        // Assert
        Assert.Equal(0x00000008u, (uint)value);
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
        Assert.Equal(0, controlFlagsOffset.ToInt32()); // First field at offset 0
        Assert.Equal(4, cpuRateOffset.ToInt32());      // Second field at offset 4
        Assert.Equal(8, maxRateOffset.ToInt32());      // Third field at offset 8
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
