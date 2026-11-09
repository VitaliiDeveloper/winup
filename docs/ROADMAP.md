# Roadmap

What comes next for winup, in priority order. The same rules apply to everything here: a check changes nothing, everything
deleted goes to the Recycle Bin, open apps are skipped, links are never followed, and administrator rights are asked for one
action at a time.

## Done in 0.1

- App and command line on one core; `winup check`, `clean`, `history`, `rules`.
- Cleanup in three levels (`safe`, `standard`, `aggressive`), rules as data, two phases with a re-check of every path right before deleting.
- Restart Manager to find apps that hold files open.
- Caches of temp files, Explorer thumbnails, GPU shaders, crash dumps, Electron and other Chromium-based apps, game launchers,
  package managers and browsers.
- Reports and `history.jsonl`; interface in English and Ukrainian; demo mode (`--demo`) with sample data; 38 tests on fake folder trees.

## 1. Health and security checks

Report only. Fixes are printed, never run.

- **Disk:** free space, disk health (SMART, `Get-PhysicalDisk` and reliability counters).
- **Memory, CPU, battery** (`powercfg /batteryreport`).
- **Security:** BitLocker, Microsoft Defender and Firewall, Secure Boot, UAC and SmartScreen.
- **Backup:** File History / backup and restore points.
- **What starts with Windows:** startup apps and scheduled tasks.
- **Claude Code MCP servers.**
- **Pending reboot and Windows Update status.**

## 2. Administrator-only system cleanups

Elevated for that one action, never for the whole app.

- **Clean:** Windows Update download cache (`SoftwareDistribution\Download`), Delivery Optimization cache, component store
  (`DISM /StartComponentCleanup`, no `/ResetBase`), `Windows.old`.
- **Already detected, waiting for elevation:** Windows temporary files, minidumps, memory and live kernel dumps, NVIDIA driver downloads.
- **Measured, never deleted (with advice):** `hiberfil.sys`, the page file, the size of restore points.

## 3. Updates

Tools you don't have are skipped.

- `winget upgrade` (including the `msstore` source), global npm packages, pipx, uv tools, rustup, Claude Code and its plugins,
  GitHub CLI extensions, `wsl --update`, Scoop and Chocolatey if present.
- **Windows Update is reported, never installed.**

## 4. Deep check and measure-only items

- **Deep check** (`--deep`): largest folders, old big files in Downloads, project build artifacts.
- **Measured, never deleted:** AI models (Ollama, LM Studio, Hugging Face cache), Android SDK and emulators, Premiere and
  DaVinci Resolve render caches, WSL virtual disks (advise `wsl --manage <distro> --set-sparse true`; the `.vhdx` is never touched).

## 5. More caches

- **Developer tools:** pnpm store, Puppeteer browsers, Docker build cache and dangling images, Android Studio caches.
- **Old versions:** older patch versions in nvm-windows, fnm, Volta and pyenv-win. Versions pinned in `.nvmrc`,
  `.tool-versions` or `.python-version` stay.
- **Only when you pick them:** stale `node_modules` in project folders, emptying the Recycle Bin.
- **Logs** older than 30 days.
- **Windows apps:** new Teams, Office and Spotify caches; Chrome's on-device AI model (`OptGuideOnDeviceModel`; asks first,
  Chrome downloads it again).

## 6. Settings and distribution

- **Config file:** default level, skip and enable lists, project folders, how long to keep reports.
- **Task ids:** `--skip` / `--only` with groups (`clean.*`, `check.*`).
- **History trends** (free space, battery); `last` and `reports` commands.
- **`self-update` and `uninstall`.**
- **CI** on GitHub Actions (`windows-latest`: build and tests).
- **First release** on GitHub Releases, then a winget manifest. Code signing later.
