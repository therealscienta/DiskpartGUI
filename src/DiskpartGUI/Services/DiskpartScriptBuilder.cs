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

    /// <param name="sizeMb">Size in MB; null fills the free region the partition starts in.</param>
    /// <param name="offsetKb">Start offset in KB; null lets diskpart pick the first region that fits.</param>
    public IScriptBuilder CreatePartitionPrimary(long? sizeMb = null, long? offsetKb = null)
    {
        var line = new StringBuilder("create partition primary");
        if (sizeMb.HasValue) line.Append($" size={sizeMb.Value}");
        if (offsetKb.HasValue) line.Append($" offset={offsetKb.Value}");
        _sb.AppendLine(line.ToString());
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

    public IScriptBuilder ExtendSize(long? sizeMb = null)
    {
        _sb.AppendLine(sizeMb.HasValue ? $"extend size={sizeMb.Value}" : "extend");
        return this;
    }

    public IScriptBuilder Rescan()
    {
        _sb.AppendLine("rescan");
        return this;
    }

    /// <summary>Removes every partition and the partition table from the selected disk.</summary>
    public IScriptBuilder Clean()
    {
        _sb.AppendLine("clean");
        return this;
    }

    public string Build() => _sb.ToString();
}
