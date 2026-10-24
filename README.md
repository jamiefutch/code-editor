# CodeEditor

A terminal-based code editor built with [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) and [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

## Features

- **File operations** — New, Open, Save, Save As, Quit (with unsaved-changes prompt)
- **Edit commands** — Undo, Redo, Cut, Copy, Paste, Delete, Select All
- **Find & Replace** — via the Terminal.Gui.FindReplaceDialog
- **Syntax highlighting** — automatic language detection by file extension through TextMate grammars
- **Code folding** — brace-based folding strategy
- **Indentation** — default auto-indent strategy with configurable size and tab-to-space conversion
- **View toggles** (persisted to config):
  - Line numbers gutter
  - Fold indicators gutter
  - Word wrap
  - Show tabs / whitespace characters
  - Scrollbars
  - Virtual space (Visual Studio–style: allow cursor past end-of-line on Right/Down)
- **Large file streaming** — progressive loading with throttled progress reporting for files >1 MB; small files load synchronously before first paint
- **Persistent settings** — saved to `~/.tui/codeeditor.config.json`

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) (target framework: `net10.0`)
- C# 14 / LangVersion 14

## Project Structure

```
├── src/
│   ├── CodeEditor.slnx          # Solution file
│   ├── Directory.Build.props    # Shared MSBuild props (framework, language, nullable)
│   ├── Directory.Packages.props # Central NuGet package versioning
│   └── CodeEditor/              # Source code
│       ├── Program.cs           # Entry point; CLI arg parsing, app bootstrap
│       ├── CodeEditorApp.cs     # Main Window: menu bar, editor host, status bar
│       ├── CodeEditorApp.FileOps.cs    # New / Open / Save / Save As / Quit
│       ├── CodeEditorApp.EditCommands.cs  # Find & Replace dialog
│       ├── CodeEditorApp.ViewSettings.cs# View toggles + About dialog
│       ├── VirtualSpaceEditor.cs         # VS-style virtual space cursor support
│       ├── EditorConfig.cs               # JSON config load/save (persisted settings)
│       ├── Hosting.cs                    # Serilog → MEL logging pipeline setup
│       ├── InlineProgress.cs             # Synchronous progress wrapper for non-hosted scenarios
│       └── SaveChangesChoice.cs          # Enum for unsaved-changes dialog choice
├── scripts/
│   ├── build.sh           # Build script (Debug or Release)
│   └── run.sh             # Build + run with optional file arguments
└── logs/                  # Serilog daily-rolling log files
```

## Building & Running

### Quick start

```bash
# Build in Debug mode
./scripts/build.sh

# Run (opens empty document)
./scripts/run.sh

# Open a specific file
./scripts/run.sh path/to/file.cs

# Open read-only
./scripts/run.sh --read-only path/to/file.cs
```

### Build script options

```bash
./scripts/build.sh [--configuration Debug|Release] [--project <path>]
```

The build script uses a sandbox-compatible NuGet cache at `/tmp/nuget-packages`. Override with:

```bash
export NUGET_PACKAGES=/custom/nuget/cache
./scripts/build.sh
```

### Direct dotnet commands

```bash
dotnet restore src/CodeEditor/CodeEditor.csproj
dotnet build src/CodeEditor/CodeEditor.csproj -c Release
dotnet run --project src/CodeEditor/CodeEditor.csproj -- myfile.txt
```

## Configuration

Settings are stored in `~/.tui/codeeditor.config.json` with the following structure:

```json
{
  "EditorSettings": {
    "LineNumbers": true,
    "FoldIndicators": true,
    "WordWrap": false,
    "ShowTabs": false,
    "IndentSize": 4,
    "ConvertTabsToSpaces": true,
    "AutoIndent": true,
    "Scrollbars": true,
    "VirtualSpace": true
  }
}
```

Changes made via the **View** menu are saved automatically. The config loader also supports legacy dotted-key format (`EditorSettings.LineNumbers`) for backward compatibility.

## Logging

Serilog writes daily-rolling log files to `logs/codeeditor.log` (relative to the working directory). Log template:

```
{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}
```

Trace categories enabled by default: **Command**, **Keyboard**, **Mouse**.

## Key Bindings

| Action | Shortcut |
|---|---|
| New file | Ctrl+N |
| Open | Ctrl+O |
| Save | Ctrl+S |
| Save As | Ctrl+Shift+S |
| Find | Ctrl+F |
| Replace | Ctrl+H |
| Undo | Ctrl+Z |
| Redo | Ctrl+Y |
| Cut | Ctrl+X |
| Copy | Ctrl+C |
| Paste | Ctrl+V |
| Select All | Ctrl+A |
| Quit | Esc |

## Architecture

The main `CodeEditorApp` window is split into a **partial class** across four files:

1. **`CodeEditorApp.cs`** — constructor, menu bar layout, status bar wiring, progress reporting helpers
2. **`CodeEditorApp.FileOps.cs`** — file I/O with async streaming, save-dialog prompts, sync bridge for Terminal.Gui's SynchronizationContext
3. **`CodeEditorApp.EditCommands.cs`** — find/replace dialog invocation
4. **`CodeEditorApp.ViewSettings.cs`** — toggle handlers, config persistence, About dialog

The custom `VirtualSpaceEditor` subclass intercepts Right/Down key events to allow cursor movement past end-of-line, matching Visual Studio behavior.

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| Terminal.Gui | 2.4.17 | Cross-platform terminal GUI framework |
| Terminal.Gui.Editor | 2.5.7 | Terminal-based editor control with syntax highlighting, folding, indentation |
| Serilog | 4.1.0 | Structured logging |
| Serilog.Extensions.Logging | 8.0.0 | Bridges Serilog to Microsoft.Extensions.Logging |
| Serilog.Sinks.File | 6.0.0 | Daily-rolling file log sink |
| Microsoft.Extensions.Configuration / Json | 10.0.13 | JSON config loading |
| Microsoft.Extensions.Logging | 10.0.7 | Logging abstraction |

## License

[Add your license here]
