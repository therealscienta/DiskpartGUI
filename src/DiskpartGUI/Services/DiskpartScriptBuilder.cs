using System.Text;

namespace DiskpartGUI.Services;

public sealed class DiskpartScriptBuilder : IScriptBuilder
{
    private readonly StringBuilder _sb = new();

    public IScriptBuilder SelectDisk(int diskNumber)
    {
        _sb.AppendLine($"select disk {diskNumber}");
        return this;
    }

    public IScriptBuilder SelectPartition(int partitionNumber)
    {
        _sb.AppendLine($"select partition {partitionNumber}");
        return this;
    }

    public IScriptBuilder CreatePartitionPrimary(long? sizeMb = null)
    {
        if (sizeMb.HasValue)
            _sb.AppendLine($"create partition primary size={sizeMb.Value}");
        else
            _sb.AppendLine("create partition primary");
        return this;
    }

    public IScriptBuilder FormatPartition(string filesystem, string label, bool quick = true)
    {
        var quickStr = quick ? " quick" : string.Empty;
        // Escape label: replace double-quotes with nothing (diskpart does not support escaped quotes)
        var safeLabel = label.Replace("\"", string.Empty);
        _sb.AppendLine($"format fs={filesystem.ToLowerInvariant()} label=\"{safeLabel}\"{quickStr}");
        return this;
    }

    public IScriptBuilder AssignLetter()
    {
        _sb.AppendLine("assign");
        return this;
    }

    public IScriptBuilder DeletePartition(bool overrideProtected = false)
    {
        _sb.AppendLine(overrideProtected ? "delete partition override" : "delete partition");
        return this;
    }

    public IScriptBuilder ShrinkDesired(long mb)
    {
        _sb.AppendLine($"shrink desired={mb}");
        return this;
    }

    public IScriptBuilder ShrinkQueryMax()
    {
        _sb.AppendLine("shrink querymax");
        return this;
    }

    public IScriptBuilder ExtendSize(long mb)
    {
        _sb.AppendLine($"extend size={mb}");
        return this;
    }

    public string Build() => _sb.ToString();
}
