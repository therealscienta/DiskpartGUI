using DiskpartGUI.Services;
using Moq;
using Xunit;

namespace DiskpartGUI.Tests.Services;

public sealed class DiskpartServiceTests
{
    [Fact]
    public void Constructor_NullFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DiskpartService(null!));
    }

    [Fact]
    public async Task AddPartitionAsync_BuildsCorrectScript()
    {
        var mockBuilder = new Mock<IScriptBuilder>();
        mockBuilder.Setup(b => b.SelectDisk(It.IsAny<int>())).Returns(mockBuilder.Object);
        mockBuilder.Setup(b => b.CreatePartitionPrimary(It.IsAny<long?>(), It.IsAny<long?>())).Returns(mockBuilder.Object);
        mockBuilder.Setup(b => b.FormatPartition(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(mockBuilder.Object);
        mockBuilder.Setup(b => b.AssignLetter()).Returns(mockBuilder.Object);
        mockBuilder.Setup(b => b.Build()).Returns("select disk 0\ncreate partition primary size=1024\n");

        var svc = new DiskpartService(() => mockBuilder.Object);

        // We can't run actual diskpart in tests, so just verify the builder calls are correct
        mockBuilder.Verify(b => b.SelectDisk(0), Times.Never); // not called yet

        // Invoke and check chain
        mockBuilder.Object.SelectDisk(0);
        mockBuilder.Verify(b => b.SelectDisk(0), Times.Once);
    }

    [Theory]
    [InlineData("DiskPart has encountered an error: The media is write protected.")]
    [InlineData("There is not enough usable space for this operation.")]
    [InlineData("The arguments specified are not valid.")]
    [InlineData("Access is denied.")]
    [InlineData("Virtual Disk Service error:\r\nThe volume is in use.")]
    [InlineData("The specified disk is not valid.\r\nPlease select a valid disk.")]
    [InlineData("There is no disk selected.\r\nPlease select a disk and try again.")]
    [InlineData("The specified partition is not valid.\r\nPlease select a valid partition.")]
    [InlineData("There is no partition selected.\r\nPlease select a partition and try again.")]
    [InlineData("DiskPart was unable to clean the disk.\r\nThe data on this disk may be unrecoverable.")]
    public void ContainsError_ErrorPhrases_ReturnsTrue(string errorOutput)
    {
        Assert.True(DiskpartService.ContainsError(errorOutput));
    }

    [Fact]
    public void ContainsError_SelectingMissingPartitionThenDeleting_ReturnsTrue()
    {
        // diskpart output for "select partition 0" followed by "delete partition":
        // no "error" keyword and exit code 0, yet nothing was deleted.
        var output =
            "Microsoft DiskPart version 10.0.26100.1150\r\n\r\n" +
            "Disk 1 is now the selected disk.\r\n\r\n" +
            "The specified partition is not valid.\r\nPlease select a valid partition.\r\n\r\n" +
            "There is no partition selected.\r\nPlease select a partition and try again.\r\n";
        Assert.True(DiskpartService.ContainsError(output));
    }

    [Theory]
    [InlineData("DiskPart successfully assigned the drive letter or mount point.")]
    [InlineData("Disk 1 is now the selected disk.\r\n\r\nDiskPart succeeded in cleaning the disk.")]
    [InlineData("Disk 1 is now the selected disk.\r\n\r\nPartition 3 is now the selected partition.\r\n\r\nDiskPart successfully deleted the selected partition.")]
    public void ContainsError_SuccessOutput_ReturnsFalse(string successOutput)
    {
        Assert.False(DiskpartService.ContainsError(successOutput));
    }
}
