using DiskpartGUI.ViewModels.Infrastructure;

namespace DiskpartGUI.ViewModels;

public sealed class AddPartitionViewModel : ViewModelBase
{
    private long _sizeMb;
    private string _label = "New Volume";
    private string _fileSystem = "NTFS";

    public long AvailableSpaceMb { get; }

    public long SizeMb
    {
        get => _sizeMb;
        set
        {
            if (SetProperty(ref _sizeMb, value))
            {
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(ValidationMessage));
            }
        }
    }

    public string Label
    {
        get => _label;
        set
        {
            if (SetProperty(ref _label, value))
            {
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(ValidationMessage));
            }
        }
    }

    public string FileSystem
    {
        get => _fileSystem;
        set
        {
            if (SetProperty(ref _fileSystem, value))
            {
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(ValidationMessage));
            }
        }
    }

    public IReadOnlyList<string> FileSystems { get; } = ["NTFS", "FAT32", "exFAT"];

    /// <summary>
    /// diskpart's <c>format fs=fat32</c> refuses volumes larger than 32 GB. The partition would
    /// already be created by then, leaving an unformatted partition behind.
    /// </summary>
    public const long MaxFat32SizeMb = 32 * 1024;

    private bool IsFatFamily =>
        string.Equals(FileSystem, "FAT32", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(FileSystem, "exFAT", StringComparison.OrdinalIgnoreCase);

    private bool ExceedsFat32Limit =>
        string.Equals(FileSystem, "FAT32", StringComparison.OrdinalIgnoreCase) && SizeMb > MaxFat32SizeMb;

    /// <summary>Longest volume label each filesystem accepts.</summary>
    public int MaxLabelLength => IsFatFamily ? 11 : 32;

    // FAT/exFAT labels can't contain these; a double quote would also break the diskpart script.
    private const string FatForbiddenLabelChars = "\"*+,./:;<=>?[\\]|";

    /// <summary>
    /// A bad label makes <c>format</c> fail after <c>create partition</c> has already run,
    /// leaving an unformatted partition behind, so it is rejected here instead.
    /// </summary>
    private string? LabelProblem
    {
        get
        {
            var label = Label ?? string.Empty;
            if (label.Length > MaxLabelLength)
                return $"{FileSystem} labels can be at most {MaxLabelLength} characters.";
            if (label.Any(char.IsControl))
                return "The label can't contain line breaks or control characters.";
            if (label.Contains('"'))
                return "The label can't contain double quotes.";
            if (IsFatFamily && label.IndexOfAny(FatForbiddenLabelChars.ToCharArray()) >= 0)
                return $"{FileSystem} labels can't contain any of these: \" * + , . / : ; < = > ? [ \\ ] |";
            return null;
        }
    }

    public bool IsValid => SizeMb > 0 && SizeMb <= AvailableSpaceMb && !ExceedsFat32Limit && LabelProblem is null;

    public string? ValidationMessage => ExceedsFat32Limit
        ? $"FAT32 volumes can be at most {MaxFat32SizeMb:N0} MB (32 GB). Use exFAT or NTFS, or reduce the size."
        : LabelProblem;

    public AddPartitionViewModel(long availableSpaceMb)
    {
        AvailableSpaceMb = availableSpaceMb;
        _sizeMb = Math.Min(1024, availableSpaceMb); // default to 1 GB or max available
    }
}
