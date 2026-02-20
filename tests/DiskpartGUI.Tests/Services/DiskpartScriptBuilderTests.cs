using DiskpartGUI.Services;
using Xunit;

namespace DiskpartGUI.Tests.Services;

public sealed class DiskpartScriptBuilderTests
{
    private static DiskpartScriptBuilder Builder() => new();

    [Fact]
    public void SelectDisk_ProducesCorrectLine()
    {
        var script = Builder().SelectDisk(2).Build();
        Assert.Contains("select disk 2", script);
    }

    [Fact]
    public void SelectPartition_ProducesCorrectLine()
    {
        var script = Builder().SelectPartition(3).Build();
        Assert.Contains("select partition 3", script);
    }

    [Fact]
    public void CreatePartitionPrimary_WithSize_IncludesSizeParameter()
    {
        var script = Builder().CreatePartitionPrimary(10240).Build();
        Assert.Contains("create partition primary size=10240", script);
    }

    [Fact]
    public void CreatePartitionPrimary_WithoutSize_OmitsSizeParameter()
    {
        var script = Builder().CreatePartitionPrimary().Build();
        Assert.Contains("create partition primary", script);
        Assert.DoesNotContain("size=", script);
    }

    [Fact]
    public void FormatPartition_QuickMode_IncludesQuickFlag()
    {
        var script = Builder().FormatPartition("ntfs", "My Label", quick: true).Build();
        Assert.Contains("format fs=ntfs", script);
        Assert.Contains("label=\"My Label\"", script);
        Assert.Contains("quick", script);
    }

    [Fact]
    public void FormatPartition_NotQuick_OmitsQuickFlag()
    {
        var script = Builder().FormatPartition("fat32", "Data", quick: false).Build();
        Assert.Contains("format fs=fat32", script);
        Assert.DoesNotContain("quick", script);
    }

    [Fact]
    public void FormatPartition_FilesystemLowercased()
    {
        var script = Builder().FormatPartition("NTFS", "Vol", quick: true).Build();
        Assert.Contains("fs=ntfs", script);
    }

    [Fact]
    public void FormatPartition_LabelWithQuotes_StripsInnerQuotes()
    {
        var script = Builder().FormatPartition("ntfs", "My \"Label\"", quick: true).Build();
        Assert.Contains("label=\"My Label\"", script);
    }

    [Fact]
    public void AssignLetter_ProducesAssignLine()
    {
        var script = Builder().AssignLetter().Build();
        Assert.Contains("assign", script);
    }

    [Fact]
    public void DeletePartition_WithoutOverride_NoOverrideKeyword()
    {
        var script = Builder().DeletePartition(overrideProtected: false).Build();
        Assert.Contains("delete partition", script);
        Assert.DoesNotContain("override", script);
    }

    [Fact]
    public void DeletePartition_WithOverride_IncludesOverrideKeyword()
    {
        var script = Builder().DeletePartition(overrideProtected: true).Build();
        Assert.Contains("delete partition override", script);
    }

    [Fact]
    public void ShrinkDesired_UsesMbValue()
    {
        var script = Builder().ShrinkDesired(5120).Build();
        Assert.Contains("shrink desired=5120", script);
    }

    [Fact]
    public void ShrinkQueryMax_ProducesCorrectLine()
    {
        var script = Builder().ShrinkQueryMax().Build();
        Assert.Contains("shrink querymax", script);
    }

    [Fact]
    public void ExtendSize_UsesMbValue()
    {
        var script = Builder().ExtendSize(3072).Build();
        Assert.Contains("extend size=3072", script);
    }

    [Fact]
    public void Build_CompleteAddPartitionScript_ProducesExpectedOutput()
    {
        var script = Builder()
            .SelectDisk(0)
            .CreatePartitionPrimary(10240)
            .FormatPartition("ntfs", "New Volume", quick: true)
            .AssignLetter()
            .Build();

        var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("select disk 0", lines[0]);
        Assert.Equal("create partition primary size=10240", lines[1]);
        Assert.Contains("format fs=ntfs", lines[2]);
        Assert.Equal("assign", lines[3]);
    }

    [Fact]
    public void Build_CompleteDeleteScript_ProducesExpectedOutput()
    {
        var script = Builder()
            .SelectDisk(1)
            .SelectPartition(2)
            .DeletePartition(overrideProtected: true)
            .Build();

        var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("select disk 1", lines[0]);
        Assert.Equal("select partition 2", lines[1]);
        Assert.Equal("delete partition override", lines[2]);
    }

    [Fact]
    public void Build_ShrinkScript_ProducesExpectedOutput()
    {
        var script = Builder()
            .SelectDisk(0)
            .SelectPartition(1)
            .ShrinkDesired(5120)
            .Build();

        var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("select disk 0", lines[0]);
        Assert.Equal("select partition 1", lines[1]);
        Assert.Equal("shrink desired=5120", lines[2]);
    }

    [Fact]
    public void Build_ExtendScript_ProducesExpectedOutput()
    {
        var script = Builder()
            .SelectDisk(0)
            .SelectPartition(1)
            .ExtendSize(3072)
            .Build();

        var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("extend size=3072", lines[2]);
    }

    [Fact]
    public void Builder_IsFluentAndChainable()
    {
        // Verify each method returns the same builder instance
        var builder = Builder();
        var result = builder
            .SelectDisk(0)
            .SelectPartition(1)
            .ShrinkDesired(100);
        Assert.Same(builder, result);
    }
}
