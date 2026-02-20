# CLAUDE.md — DiskpartGUI

Context for AI assistants working in this repository.

## Project Overview

C# WPF desktop application — a GUI frontend for the Windows `diskpart` command-line tool plus raw Win32 disk I/O. Users can view disk and partition information, and perform add, delete, resize, and move partition operations.

**Target:** Windows 10 1809+ x64, .NET 10, WPF

---

## Build Commands

```powershell
# Restore + build
dotnet restore DiskpartGUI.sln
dotnet build DiskpartGUI.sln --configuration Release

# Run all tests
dotnet test tests/DiskpartGUI.Tests/DiskpartGUI.Tests.csproj --configuration Release

# Publish portable single exe
dotnet publish src/DiskpartGUI/DiskpartGUI.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true --output ./publish/portable
```

---

## Key Architecture Decisions

### Why WMI for reads, diskpart for writes?
- WMI provides structured, parseable data via `Win32_DiskDrive`, `Win32_DiskPartition`, `Win32_LogicalDisk`
- diskpart is the proven, documented tool for partition modification
- Combining both avoids the fragility of parsing all diskpart output while keeping write operations reliable

### Why manual MVVM (no CommunityToolkit)?
- Keeps the project dependency-free beyond `System.Management`
- `ViewModelBase`, `RelayCommand`, `AsyncRelayCommand` are in `ViewModels/Infrastructure/`
- Simple and transparent — no source generators or magic attributes

### Why no DI container?
- The application has a shallow dependency graph
- Manual composition in `App.xaml.cs` `OnStartup` is clear and sufficient
- Avoids pulling in a DI framework for ~5 services

### Why diskpart script files instead of stdin pipe?
- Script file mode (`diskpart /s <file>`) is the officially documented approach
- stdin pipe mode is unreliable for output capture
- Scripts are written to `Path.GetTempPath()` as UTF-8 without BOM and deleted after execution

---

## Critical Gotchas

### diskpart exit code is always 0
diskpart returns exit code 0 even when operations fail. **Never** rely on exit code to detect errors.
Always parse stdout for error phrases. See `DiskpartService.ContainsError()` for the list.

### WMI must not run on the UI thread
WPF uses STA; WMI queries can deadlock. Always wrap WMI calls in `Task.Run(...)`.
See `WmiDiskService` — all public methods delegate to `Task.Run`.

### Elevation comes from app.manifest, not Process.Verb
`app.manifest` sets `requestedExecutionLevel level="requireAdministrator"`.
Do **NOT** set `ProcessStartInfo.Verb = "runas"` when launching diskpart — this causes a second UAC prompt.

### WMI size fields return UInt64, not long
`ManagementObject["Size"]` is `UInt64` boxed as `object`. Cast with `Convert.ToInt64(obj["Size"])`, not `(long)`.

### MSIX + admin elevation
The packaging project uses `rescap:Capability Name="runFullTrust"` which allows the app manifest's `requireAdministrator` to function inside MSIX. Without `runFullTrust`, the MSIX sandbox blocks elevation.

### "Move Partition" uses raw sector I/O, not diskpart
diskpart has no move command. The move feature opens `\\.\PhysicalDriveN` directly via Win32 `ReadFile`/`WriteFile`.
Copy direction matters: moving left → copy forward, moving right → copy backward (same as `memmove`).
The partition table is updated **after** the full copy so cancellation leaves the source intact.

### `shrink querymax` must be called before opening the Resize dialog
diskpart always exits 0; it silently shrinks less than requested if unmovable files block it.
`DiskpartService.QueryShrinkMaxAsync` runs `shrink querymax` first and parses the MB value.
`ResizePartitionViewModel.MaxShrinkMb` (0 = unknown/failed) gates `IsValid` and the slider minimum.

### ObservableCollection mutations need the UI thread
After async WMI results return, use `Application.Current.Dispatcher.Invoke(...)` to update collections.
See `MainViewModel.RefreshAsync()`.

---

## Versioning

The project uses **Semantic Versioning** (`MAJOR.MINOR.PATCH`). The version in `src/DiskpartGUI/DiskpartGUI.csproj` is patched automatically by the CI release workflow — you do **not** commit the bumped version. You only tag.

### Bump rules

| Change type | Bump |
|---|---|
| Crash fix, binding error, wrong output parsing, label/text correction | **Patch** (1.0.**X**) |
| UI tweak, performance improvement, no new user-visible feature | **Patch** |
| New partition operation (Add, Delete, Resize, Move) | **Minor** (1.**X**.0) |
| New dialog, new toolbar item, new user-visible capability | **Minor** |
| Enhancement to existing feature (e.g. shrink querymax, progress reporting) | **Minor** |
| Replacing WMI or diskpart with a different backend | **Major** (**X**.0.0) |
| Dropping Windows version support or .NET version upgrade that breaks compat | **Major** |
| Complete UI redesign affecting all screens | **Major** |

Pre-release suffix examples: `v1.2.0-beta.1`, `v1.2.0-rc.1` (GitHub marks these as pre-release automatically).

### Version evaluation checklist (before tagging)

1. Review commits since the last tag: `git log v1.0.0..HEAD --oneline`
2. Apply the bump rules above — if multiple change types, use the **highest** bump
3. Decide on the new version string (e.g. `1.1.0`)
4. Run `dotnet test` one last time to confirm all tests pass
5. Tag and push:
   ```bash
   git tag v1.1.0
   git push origin v1.1.0
   ```
   The `release.yml` workflow fires automatically, patches the csproj, builds, and creates the GitHub Release.

The `<Version>` in `DiskpartGUI.csproj` always stays at the last released value in the repo (or `1.0.0` initially). You do **not** manually edit it — CI does.

---

## Project Structure

```
src/DiskpartGUI/
├── app.manifest                    # requireAdministrator + DPI aware
├── App.xaml / App.xaml.cs         # Composition root (manual DI)
├── Models/                         # Immutable records: DiskInfo, PartitionInfo, LogicalDiskInfo,
│                                   #   FreeSpaceRegion, MoveProgress
├── Interop/
│   └── NativeDisk.cs               # All Win32 P/Invoke: CreateFile, ReadFile, WriteFile,
│                                   #   SetFilePointerEx, DeviceIoControl, IOCTL constants,
│                                   #   layout buffer helpers (BinaryPrimitives, no unsafe)
├── Services/
│   ├── IDiskService.cs             # WMI read interface
│   ├── IPartitionService.cs        # diskpart write interface (add/delete/resize/querymax)
│   ├── IPartitionMoveService.cs    # Raw move interface
│   ├── IScriptBuilder.cs           # Fluent script builder interface
│   ├── IDialogService.cs           # Dialog abstraction (for testability)
│   ├── WmiDiskService.cs           # WMI implementation
│   ├── DiskpartService.cs          # diskpart execution + QueryShrinkMaxAsync
│   ├── DiskpartScriptBuilder.cs    # Script builder (fluent, chainable)
│   ├── RawDiskMoveService.cs       # Raw sector copy + partition table update
│   └── WpfDialogService.cs         # Concrete WPF dialog service
├── ViewModels/
│   ├── Infrastructure/             # ViewModelBase, RelayCommand, AsyncRelayCommand
│   ├── MainViewModel.cs            # Root VM
│   ├── DiskItemViewModel.cs        # Per-disk VM
│   ├── PartitionItemViewModel.cs   # Per-partition VM (includes RelativeWidth for DiskBar)
│   ├── AddPartitionViewModel.cs    # Dialog VM
│   ├── DeletePartitionViewModel.cs # Dialog VM
│   ├── ResizePartitionViewModel.cs # Dialog VM (MaxShrinkMb, MinNewSizeMb from querymax)
│   └── MovePartitionViewModel.cs   # Dialog VM (two-phase: configure → in-progress)
├── Views/
│   ├── MainWindow.xaml             # Shell: toolbar, disk list, detail panel, status bar
│   ├── DiskDetailView.xaml         # Disk bar + partition DataGrid
│   ├── Dialogs/                    # Add/Delete/Resize/Move/About dialogs (Window)
│   └── Controls/DiskBar.xaml      # Proportional partition visualization
├── Converters/                     # BytesToHuman, BoolToVisibility, PartitionTypeToColor
└── Resources/                      # Styles.xaml, Colors.xaml

tests/DiskpartGUI.Tests/
├── Services/                       # DiskpartScriptBuilderTests, DiskpartServiceTests,
│                                   #   RawDiskMoveServiceTests
├── ViewModels/                     # MainViewModelTests, dialog VM tests,
│                                   #   MovePartitionViewModelTests
└── Infrastructure/                 # RelayCommandTests, AsyncRelayCommandTests
```

---

## Testing Notes

- Unit tests use **xUnit** + **Moq**
- WMI tests are tagged `[Trait("Category", "Integration")]` and excluded from CI
- CI filter: `--filter "Category!=Integration"`
- `MainViewModel` tests mock `IDiskService`, `IPartitionService`, `IPartitionMoveService`, and `IDialogService`
- Do NOT mock `DiskpartScriptBuilder` in script builder tests — test the real implementation
- `AsyncRelayCommand` tests use `TaskCompletionSource` to control async timing

---

## What NOT to Do

- Do not run WMI queries on the UI thread
- Do not check diskpart exit code for success/failure — parse output instead
- Do not use `ProcessStartInfo.Verb = "runas"` — elevation is from app.manifest
- Do not write diskpart scripts with UTF-8 BOM (use `new UTF8Encoding(false)`)
- Do not attempt stdin pipe to diskpart — use script files only
- Do not store disk state outside the ViewModel refresh cycle — always re-read from WMI after mutations
- Do not update `ObservableCollection` from a background thread without Dispatcher.Invoke
