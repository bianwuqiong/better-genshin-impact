using BetterGenshinImpact.GameTask.AutoDomain;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomainTests;

public class PetrifiedTreeAlignmentTests
{
    [Theory]
    [InlineData(900, 120, 0)]
    [InlineData(808, 112, 0)]
    [InlineData(120, 140, 1)]
    [InlineData(1400, 170, 2)]
    public void DeterminePetrifiedTreeAlignment_UsesLateralMovementForLargeOffsets(
        int x, int width, int expected)
    {
        var result = AutoDomainTask.DeterminePetrifiedTreeAlignment(
            new Rect(x, 100, width, 220), 1920);

        Assert.Equal(expected, (int)result);
    }

    [Fact]
    public void DeterminePetrifiedTreeAlignment_RejectsInvalidCaptureWidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AutoDomainTask.DeterminePetrifiedTreeAlignment(new Rect(0, 0, 100, 100), 0));
    }
}
