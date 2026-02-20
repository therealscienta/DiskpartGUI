namespace DiskpartGUI.Services;

public interface IScriptBuilder
{
    IScriptBuilder SelectDisk(int diskNumber);
    IScriptBuilder SelectPartition(int partitionNumber);
    IScriptBuilder CreatePartitionPrimary(long? sizeMb = null);
    IScriptBuilder FormatPartition(string filesystem, string label, bool quick = true);
    IScriptBuilder AssignLetter();
    IScriptBuilder DeletePartition(bool overrideProtected = false);
    IScriptBuilder ShrinkDesired(long mb);
    IScriptBuilder ShrinkQueryMax();
    IScriptBuilder ExtendSize(long mb);
    string Build();
}
