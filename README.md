# Roblox Multi-Instance

A small tray app that does the Process Explorer steps for you: it watches for `RobloxPlayerBeta.exe` and closes the `ROBLOX_singletonEvent` handle inside it, so the next Roblox launch doesn't see an existing instance and quit.

## Use

```powershell
.\build.ps1                 # builds dist\RobloxMultiInstance.exe (uses the compiler that ships with Windows)
.\dist\RobloxMultiInstance.exe
```

The Roblox icon with a red slash appears in the system tray (check the `^` overflow). It's read from your installed Roblox client at runtime, so no Roblox artwork is included in this repo. Right-click it:

- **Start with Windows**: adds/removes a `HKCU\...\Run` entry pointing at the exe's current location. Re-toggle it if you move the exe.
- **Pause**: stops closing handles until unticked.
- **Open log**: `dist\RobloxMultiInstance.log` records every process it sees and every handle it closes (or why it couldn't).
- **Exit**: stops it.

Then launch Roblox normally, as many times as you like. Each new `RobloxPlayerBeta.exe` gets its singleton event closed within ~250 ms of appearing, with no manual steps.

No admin rights needed, as long as Roblox itself isn't running as admin. If it is, run this elevated too (the log will show `Win32 error 5` otherwise).

## How it works

Same as the manual method: find the handle named `...\ROBLOX_singletonEvent` in each Roblox process and close it (`DuplicateHandle` with `DUPLICATE_CLOSE_SOURCE`). A background thread polls for Roblox processes every 250 ms, enumerates the system handle table only while a Roblox process still has its event open, and stops scanning a process once it's done.

It doesn't touch `ROBLOX_singletonMutex`. Every running client holds that one, and it doesn't block a second launch.

## Test flags

`--dry-run` logs what it would close without closing anything. `--process NAME` and `--object NAME` (repeatable) override the process and object names it looks for.

## Notes

- Only the classic Win32 client (`RobloxPlayerBeta.exe`) is supported, not the Microsoft Store version.
- Running multiple instances may be against Roblox's rules or be detected by their anti-cheat; use it at your own risk.
