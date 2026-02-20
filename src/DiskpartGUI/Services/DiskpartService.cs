using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DiskpartGUI.Services;

public sealed class DiskpartService : IPartitionService
{
    private static readonly string[] ErrorPhrases =
    [
        "DiskPart has encountered an error",
        "There is not enough usable space",
        "The arguments specified are not valid",
        "Access is denied",
        "Virtual Disk Service error",
    ];

    private readonly Func<IScriptBuilder> _builderFactory;

    public DiskpartService(Func<IScriptBuilder> builderFactory)
    {
        _builderFactory = builderFactory ?? throw new ArgumentNullException(nameof(builderFactory));
    }

    public Task<DiskpartResult> AddPartitionAsync(
        int diskNumber, long sizeMb, string? label, string filesystemType, CancellationToken ct = default)
    {
        var builder = _builderFactory()
            .SelectDisk(diskNumber)
            .CreatePartitionPrimary(sizeMb)
            .FormatPartition(filesystemType, label ?? "New Volume", quick: true)
            .AssignLetter();

        return ExecuteScriptAsync(builder.Build(), ct);
    }

    public Task<DiskpartResult> DeletePartitionAsync(
        int diskNumber, int partitionNumber, bool forceOverride = false, CancellationToken ct = default)
    {
        var builder = _builderFactory()
            .SelectDisk(diskNumber)
            .SelectPartition(partitionNumber)
            .DeletePartition(overrideProtected: forceOverride);

        return ExecuteScriptAsync(builder.Build(), ct);
    }

    public async Task<long> QueryShrinkMaxAsync(
        int diskNumber, int partitionNumber, CancellationToken ct = default)
    {
        var builder = _builderFactory()
            .SelectDisk(diskNumber)
            .SelectPartition(partitionNumber)
            .ShrinkQueryMax();

        var result = await ExecuteScriptAsync(builder.Build(), ct);
        return ParseQueryMaxMb(result.Output);
    }

    private static long ParseQueryMaxMb(string output)
    {
        // diskpart output: "The maximum number of reclaimable bytes is:  51200 MB"
        //               or "The maximum number of reclaimable bytes is:  43016 Megabytes"
        var line = output.Split('\n')
            .FirstOrDefault(l => l.Contains("reclaimable", StringComparison.OrdinalIgnoreCase));
        if (line is null) return 0;
        var match = Regex.Match(line, @"(\d+)\s*(?:MB|Megabytes)", RegexOptions.IgnoreCase);
        return match.Success ? long.Parse(match.Groups[1].Value) : 0;
    }

    public Task<DiskpartResult> ShrinkPartitionAsync(
        int diskNumber, int partitionNumber, long shrinkMb, CancellationToken ct = default)
    {
        var builder = _builderFactory()
            .SelectDisk(diskNumber)
            .SelectPartition(partitionNumber)
            .ShrinkDesired(shrinkMb);

        return ExecuteScriptAsync(builder.Build(), ct);
    }

    public Task<DiskpartResult> ExtendPartitionAsync(
        int diskNumber, int partitionNumber, long extendMb, CancellationToken ct = default)
    {
        var builder = _builderFactory()
            .SelectDisk(diskNumber)
            .SelectPartition(partitionNumber)
            .ExtendSize(extendMb);

        return ExecuteScriptAsync(builder.Build(), ct);
    }

    internal async Task<DiskpartResult> ExecuteScriptAsync(string scriptContent, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"diskpart_{Guid.NewGuid():N}.txt");
        try
        {
            // Write UTF-8 without BOM — diskpart can choke on BOM
            await File.WriteAllTextAsync(tempPath, scriptContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "diskpart.exe",
                    Arguments = $"/s \"{tempPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    // Do NOT set Verb = "runas" — elevation from app.manifest
                }
            };

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            var output = await outputTask;
            var error = await errorTask;

            // diskpart always exits 0; detect errors by parsing output text
            var success = !ContainsError(output);

            return new DiskpartResult(success, output, string.IsNullOrWhiteSpace(error) ? null : error);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    private static bool ContainsError(string output)
        => ErrorPhrases.Any(phrase => output.Contains(phrase, StringComparison.OrdinalIgnoreCase));
}
