# Roblox Multi-Instance

Run more than one Roblox client at once on Windows. A small tray app does the Process Explorer steps for you: it watches for `RobloxPlayerBeta.exe` and closes the `ROBLOX_singletonEvent` handle inside it, so the next Roblox launch doesn't see an existing instance and quit.

## Install

No build, install or admin rights needed. Windows 10/11 (64-bit) already has everything it runs on.

1. Download `RobloxMultiInstance.exe` from the [latest release](https://github.com/ainere/Roblox-Multiprocess/releases/latest).
2. Move it to a folder you'll keep it in, for example `%LOCALAPPDATA%\RobloxMultiInstance`. "Start with Windows" remembers where the exe is, and it writes its log next to itself, so avoid `Program Files`.
3. Run it. The exe isn't code-signed, so Windows SmartScreen may warn: click **More info**, then **Run anyway**. If you'd rather not run a downloaded binary, [build it yourself](#build-from-source), which takes one command.
4. Right-click the tray icon and tick **Start with Windows**.
5. Launch Roblox as many times as you like.

The Roblox icon with a red slash appears in the system tray (check the `^` overflow). It's read from your installed Roblox client at runtime, so no Roblox artwork is included in this repo. Right-click it:

- **Unlocked N instances**: how many Roblox processes it has freed since it started.
- **Pause**: stops closing handles until unticked.
- **Start with Windows**: adds/removes a `HKCU\...\Run` entry pointing at the exe's current location. Re-toggle it if you move the exe.
- **Open log**: `RobloxMultiInstance.log` next to the exe records every process it sees and every handle it closes (or why it couldn't).
- **Exit**: stops it.

Each new `RobloxPlayerBeta.exe` gets its singleton event closed within ~250 ms of appearing. If Roblox itself runs as admin, run this elevated too (the log shows `Win32 error 5` otherwise).

## How it works

Same as the manual method: find the handle named `...\ROBLOX_singletonEvent` in each Roblox process and close it (`DuplicateHandle` with `DUPLICATE_CLOSE_SOURCE`). A background thread polls for Roblox processes every 250 ms, enumerates the system handle table only while a Roblox process still has its event open, and stops scanning a process once it's done.

It doesn't touch `ROBLOX_singletonMutex`. Every running client holds that one, and it doesn't block a second launch.

## Build from source

```powershell
.\build.ps1    # writes dist\RobloxMultiInstance.exe, using the C# compiler that ships with Windows
```

### Test flags

`--dry-run` logs what it would close without closing anything. `--process NAME` and `--object NAME` (repeatable) override the process and object names it looks for.

## Notes

- Only the classic Win32 client (`RobloxPlayerBeta.exe`) is supported, not the Microsoft Store version.
- Running multiple instances may be against Roblox's rules or be detected by their anti-cheat; use it at your own risk.
