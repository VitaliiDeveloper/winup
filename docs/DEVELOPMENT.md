# Development

## Requirements

- Windows 10 2004 (build 19041) or newer, x64.
- [.NET 10 SDK](https://dotnet.microsoft.com/download).
- Optional: Visual Studio 2026 with the WinUI workload, for the XAML designer and Hot Reload. The command line is enough.

## Build, test, run

```powershell
dotnet build winup.slnx
dotnet test tests/Winup.Tests
dotnet run --project src/Winup.Cli -- check
dotnet run --project src/Winup.App
```

Add `--demo` to either to see sample data instead of your PC (nothing is read or changed).

## Projects

| Project | What it is |
|---|---|
| `src/Winup.Core` | Rules as data, scanner, safety policy, cleaner, Recycle Bin, reports, translations, demo data |
| `src/Winup.Cli` | The `winup` command line |
| `src/Winup.App` | The app: WinUI 3 (Windows App SDK), unpackaged and self-contained |
| `tests/Winup.Tests` | xUnit tests on throwaway fake folder trees; the real profile is never touched |

## How cleanup works

1. **Rules** live in [`rules.json`](../src/Winup.Core/Data/rules.json): a level, a category, folder patterns
   (`%LOCALAPPDATA%\…`, `{a,b}`, `*`), and the processes that must not be running.
2. **A check** (`Scanner`) expands the patterns, never through a link, measures what a cleanup would remove and asks which
   apps hold the files open. It changes nothing.
3. **A cleanup** (`Cleaner`) asks again whether the app is open and checks every single path with
   [`SafetyPolicy.cs`](../src/Winup.Core/SafetyPolicy.cs) right before moving it to the Recycle Bin. The allowed places and
   protected names live in code, so a bad rule cannot reach anything else.
4. Each cleanup writes a Markdown report and a line in `history.jsonl` under `%LOCALAPPDATA%\winup`.

## Adding a cache

1. Add a rule to `rules.json`.
2. Add its label and note, with a Ukrainian translation, to [`strings.json`](../src/Winup.Core/Data/strings.json).
3. Run the tests. They fail if a rule points outside the allowed places or a string has no translation.

## Screenshots for the docs

Both come from the demo mode, so they never show your own apps:

```powershell
powershell -ExecutionPolicy Bypass -File tools\screenshots.ps1   # app screenshots in docs/images
powershell -ExecutionPolicy Bypass -File tools\cli-svg.ps1       # docs/images/cli-check.svg
```

## App icon

A bitten disk: the cache got eaten. The sources are two SVGs in [`src/Winup.App/Assets`](../src/Winup.App/Assets):
`icon.svg` for 32 px and up, and `icon-small.svg` for 16 and 24 px, with a bigger bite and hole so they still read at that
size. After editing either one, rebuild `app.ico` (16 to 256 px) and `docs/images/icon.png`:

```powershell
powershell -ExecutionPolicy Bypass -File tools\icon.ps1
```

It rasterizes each size with headless Edge or Chrome. `app.ico` is the icon of both `.exe` files and of the app window in
the taskbar; the title bar shows `icon-small.svg`.
