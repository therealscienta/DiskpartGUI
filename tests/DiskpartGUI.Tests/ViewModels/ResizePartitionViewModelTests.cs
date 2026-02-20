using DiskpartGUI.ViewModels;
using Xunit;

namespace DiskpartGUI.Tests.ViewModels;

public sealed class ResizePartitionViewModelTests
{
    // Helper: full-range shrink (no real limit known — mirrors fallback behaviour)
    private static ResizePartitionViewModel Make(long currentSizeMb, long availableFreeMb)
        => new(currentSizeMb, availableFreeMb, maxShrinkMb: currentSizeMb - 8);

    [Fact]
    public void Operation_NewSizeSmallerThanCurrent_IsShrink()
    {
        var vm = Make(currentSizeMb: 10240, availableFreeMb: 5120);
        vm.NewSizeMb = 5120;
        Assert.Equal(ResizeOperation.Shrink, vm.Operation);
    }

    [Fact]
    public void Operation_NewSizeLargerThanCurrent_IsExtend()
    {
        var vm = Make(currentSizeMb: 10240, availableFreeMb: 5120);
        vm.NewSizeMb = 12288;
        Assert.Equal(ResizeOperation.Extend, vm.Operation);
    }

    [Fact]
    public void DeltaMb_Shrink_IsPositiveDifference()
    {
        var vm = Make(currentSizeMb: 10240, availableFreeMb: 5120);
        vm.NewSizeMb = 5120;
        Assert.Equal(5120, vm.DeltaMb);
    }

    [Fact]
    public void DeltaMb_Extend_IsPositiveDifference()
    {
        var vm = Make(currentSizeMb: 10240, availableFreeMb: 5120);
        vm.NewSizeMb = 12288;
        Assert.Equal(2048, vm.DeltaMb);
    }

    [Fact]
    public void IsValid_SameSizeAsCurrent_ReturnsFalse()
    {
        var vm = Make(currentSizeMb: 10240, availableFreeMb: 5120);
        vm.NewSizeMb = 10240;
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void IsValid_BelowMinimum_ReturnsFalse()
    {
        var vm = Make(currentSizeMb: 10240, availableFreeMb: 5120);
        vm.NewSizeMb = 1; // below 8 MB minimum
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void IsValid_ExceedsMax_ReturnsFalse()
    {
        var vm = Make(currentSizeMb: 10240, availableFreeMb: 5120);
        vm.NewSizeMb = 20000; // exceeds 10240 + 5120
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void IsValid_ValidShrink_ReturnsTrue()
    {
        var vm = Make(currentSizeMb: 10240, availableFreeMb: 5120);
        vm.NewSizeMb = 8192;
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void IsValid_ValidExtend_ReturnsTrue()
    {
        var vm = Make(currentSizeMb: 10240, availableFreeMb: 5120);
        vm.NewSizeMb = 13312;
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void MaxSizeMb_IsCurrentPlusAvailable()
    {
        var vm = Make(currentSizeMb: 10240, availableFreeMb: 5120);
        Assert.Equal(15360, vm.MaxSizeMb);
    }

    [Fact]
    public void NewSizeMb_Change_RaisesOperationAndDeltaAndIsValid()
    {
        var vm = Make(currentSizeMb: 10240, availableFreeMb: 5120);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.NewSizeMb = 5120;

        Assert.Contains("Operation", raised);
        Assert.Contains("DeltaMb", raised);
        Assert.Contains("IsValid", raised);
    }

    // ── Shrink limit tests ────────────────────────────────────────────────────

    [Fact]
    public void ShrinkLimit_BelowLimit_IsInvalid()
    {
        // maxShrinkMb = 2048 → can only shrink down to 10240 - 2048 = 8192
        var vm = new ResizePartitionViewModel(10240, 5120, maxShrinkMb: 2048);
        vm.NewSizeMb = 7000; // below 8192
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void ShrinkLimit_ExactlyAtLimit_IsValid()
    {
        var vm = new ResizePartitionViewModel(10240, 5120, maxShrinkMb: 2048);
        vm.NewSizeMb = 8192; // exactly currentSizeMb - maxShrinkMb
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void MinNewSizeMb_WithLimit_IsCurrentMinusMax()
    {
        var vm = new ResizePartitionViewModel(10240, 5120, maxShrinkMb: 3000);
        Assert.Equal(7240, vm.MinNewSizeMb); // 10240 - 3000 = 7240 (> 8)
    }

    [Fact]
    public void MinNewSizeMb_LimitLargerThanPartition_ClampedToMinSizeMb()
    {
        // If maxShrinkMb >= currentSizeMb - 8, minimum is clamped to 8
        var vm = new ResizePartitionViewModel(10240, 5120, maxShrinkMb: 10240);
        Assert.Equal(8, vm.MinNewSizeMb);
    }

    [Fact]
    public void HasShrinkLimit_WhenMaxShrinkMbNonZero_IsTrue()
    {
        var vm = new ResizePartitionViewModel(10240, 5120, maxShrinkMb: 5000);
        Assert.True(vm.HasShrinkLimit);
    }

    [Fact]
    public void HasShrinkLimit_WhenMaxShrinkMbZero_IsFalse()
    {
        var vm = new ResizePartitionViewModel(10240, 5120, maxShrinkMb: 0);
        Assert.False(vm.HasShrinkLimit);
    }

    [Fact]
    public void ShrinkLimitText_WhenKnown_ContainsMbOrGb()
    {
        var vm = new ResizePartitionViewModel(10240, 5120, maxShrinkMb: 512);
        Assert.Contains("512 MB", vm.ShrinkLimitText);
    }

    [Fact]
    public void ShrinkLimitText_WhenKnownGb_ContainsGb()
    {
        var vm = new ResizePartitionViewModel(51200, 0, maxShrinkMb: 2048);
        Assert.Contains("2 GB", vm.ShrinkLimitText);
    }

    [Fact]
    public void ShrinkLimitText_WhenUnknown_IsEmpty()
    {
        var vm = new ResizePartitionViewModel(10240, 5120, maxShrinkMb: 0);
        Assert.Equal(string.Empty, vm.ShrinkLimitText);
    }
}
