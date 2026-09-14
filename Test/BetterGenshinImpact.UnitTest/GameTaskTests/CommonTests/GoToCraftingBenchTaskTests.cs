using BetterGenshinImpact.GameTask.Common.Job;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.CommonTests;

public class GoToCraftingBenchTaskTests
{
    [Theory]
    [InlineData("100 / 60", 100)]
    [InlineData("40/60", 40)]
    [InlineData("１００／６０", 100)]
    [InlineData("100/160", 100)]
    [InlineData("1007200", 100)]
    public void TryParseCraftingOriginalResin_AcceptsVerifiedInventoryFormats(
        string raw,
        int expected)
    {
        var parsed = GoToCraftingBenchTask.TryParseCraftingOriginalResin(raw, out var count);

        Assert.True(parsed);
        Assert.Equal(expected, count);
    }

    [Theory]
    [InlineData("100/40")]
    [InlineData("100/600")]
    [InlineData("material 100/60")]
    [InlineData("999/60")]
    [InlineData("")]
    public void TryParseCraftingOriginalResin_RejectsAmbiguousValues(string raw)
    {
        var parsed = GoToCraftingBenchTask.TryParseCraftingOriginalResin(raw, out var count);

        Assert.False(parsed);
        Assert.Equal(-1, count);
    }
}
