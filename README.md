# DiskpartGUI

A modern Windows desktop application providing a graphical interface for the Windows `diskpart` utility and raw disk I/O. View all physical disks and their partitions, and perform add, delete, resize, and move operations — without touching the command line.

![Build](https://github.com/therealscienta/DiskpartGUI/actions/workflows/build.yml/badge.svg)

**Disclaimer:** This project is generated using AI! You are responsible for your own data - backup anything of value before using this, or any tool that might be destructive!

---

## Features

- **View all physical disks** with model, size, interface type, and status
- **Visual partition bar** — proportional color-coded representation of each disk's layout
- **Partition details** — drive letter, label, filesystem, size, offset, and boot flags
- **Add partition** — create a new primary partition with configurable size, label, and filesystem (NTFS / FAT32 / exFAT)
- **Delete partition** — with protection for system/boot partitions and a force-override option
- **Resize partition** — shrink or extend with a real-time shrink limit from `shrink querymax` (prevents silent partial shrinks caused by unmovable files like `pagefile.sys` or VSS snapshots)
- **Move partition** — physically relocates a partition's data using raw sector I/O; safe cancellation leaves the source partition intact if aborted mid-copy
- **About dialog** — version, description, and GitHub link
- **Portable** — runs as a single `.exe` with no installation required

---

## Requirements

| | |
|---|---|
| **OS** | Windows 10 1809 (build 17763) or later |
| **Architecture** | x64 |
| **Privileges** | **Administrator** — required to run diskpart, query WMI, and perform raw disk I/O |
| **.NET runtime** | Not required for portable build (self-contained); required for framework-dependent builds |

> The application will prompt for UAC elevation on startup. This is required by diskpart and raw disk access.

---

## Download

Go to the [Releases](https://github.com/therealscienta/DiskpartGUI/releases) page and download:

- `DiskpartGUI-vX.X.X-portable-win-x64.zip` — extract and run `DiskpartGUI.exe`, no installation needed

---

## Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10)
- Windows 10/11 (WPF requires Windows)
- Visual Studio 2022 (optional — VS Code + CLI also works)

### Build

```powershell
dotnet restore DiskpartGUI.sln
dotnet build DiskpartGUI.sln --configuration Release
```

### Run

```powershell
# Must run as Administrator
dotnet run --project src/DiskpartGUI/DiskpartGUI.csproj
```

### Publish portable single exe

```powershell
dotnet publish src/DiskpartGUI/DiskpartGUI.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  --output ./publish/portable
```

---

## Running Tests

```powershell
dotnet test tests/DiskpartGUI.Tests/DiskpartGUI.Tests.csproj --configuration Release
```

---

## Architecture

```
src/DiskpartGUI/
├── Models/           Records: DiskInfo, PartitionInfo, FreeSpaceRegion, MoveProgress, …
├── Interop/          Win32 P/Invoke for raw disk I/O (NativeDisk.cs)
├── Services/         WMI disk reads + diskpart script execution + raw move service
├── ViewModels/       MVVM: MainViewModel, DiskItemViewModel, dialog VMs
├── Views/            WPF XAML: MainWindow, DiskDetailView, DiskBar, dialogs
├── Converters/       IValueConverter implementations
└── Resources/        Styles and colors
```

**Technology choices:**
- **WMI** (`Win32_DiskDrive`, `Win32_DiskPartition`, `Win32_LogicalDisk`) for structured disk reads
- **diskpart.exe** via temporary script files (`diskpart /s <file>`) for add, delete, and resize operations
- **Raw Win32 I/O** (`CreateFile`, `ReadFile`, `WriteFile`, `DeviceIoControl`) for the move operation — diskpart has no move command
- **WPF + classic MVVM** with manual `ViewModelBase` and `RelayCommand` (no framework dependencies beyond `System.Management`)

**Move partition safety model:**
1. Volume is locked and dismounted before any writes
2. All sectors are copied to the destination first (direction-aware to handle overlapping ranges)
3. Only after a successful full copy is the partition table entry updated
4. Cancellation during the copy phase leaves the source partition completely intact

---

## Safety Warning

Disk operations are **irreversible**. Deleting, resizing, or moving partitions can result in permanent data loss. Always back up important data before performing any disk operations.

The application displays warnings for system and boot partitions and requires explicit confirmation before any destructive action.

---

## Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Run tests: `dotnet test --filter "Category!=Integration"`
4. Commit and push
5. Open a Pull Request against `main`

Please ensure all unit tests pass before submitting a PR.

---

## License

[MIT](LICENSE)
