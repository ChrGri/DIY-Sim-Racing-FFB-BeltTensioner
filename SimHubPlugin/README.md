# SimHub Plugin: DIY FFB Belt Tensioner

A small SimHub plugin to set up the belt tensioner without a serial terminal. It shows up as **FFB Belt Tensioner** in the left SimHub menu and has four tabs:

| Tab | What it does |
| :--- | :--- |
| **1. Firmware** | Flashes the controller via USB with the embedded `esptool`. The firmware is downloaded from the latest [GitHub release](https://github.com/ChrGri/DIY-Sim-Racing-FFB-BeltTensioner/releases) for the selected board, or taken from a local release `.zip` / build folder. Native USB boards (1200 bps touch, new bootloader COM port) and CP2102 boards (DTR/RTS reset) are handled automatically. |
| **2. Configuration** | Reads and writes spindle pitch and working range per axis (`CONFIG`, `SET_PITCH`, `SET_RANGE`, `RESET_CONFIG`), restarts the controller to apply them, and flashes / checks the iSV57 servo parameters (`FLASH_SERVO`, `DIFF_SERVO`). |
| **3. Test** | Homing, park, servo load display (CMD 11), and a motion test that streams positions like SimHub Motion: *hold position*, *sine* (center, amplitude, frequency) and *brake pulse* (base, peak, duration, interval), per axis, with the SimHub speed / acceleration values. |
| **4. SimHub Motion** | Creates a ready-to-import `.shmotionoutput` preset (single or dual belt tensioner) with your COM port(s), direction and speed / acceleration limits, based on the presets in [`docs/motionPluginSetup`](../docs/motionPluginSetup/). |

At the bottom there is a log / serial console for any ASCII command (`HELP`, `STATUS`, `DUMP_SERVO`, ...).

> [!IMPORTANT]
> A COM port can only be used by one program. The plugin and SimHub Motion therefore never use the controller at the same time: disable SimHub Motion before you connect in the plugin, and click **Disconnect** / **Release port for SimHub Motion** before you enable SimHub Motion again.

## Installation

1. Close SimHub.
2. Copy `DiyBeltTensioner.dll` into the SimHub installation folder (usually `C:\Program Files (x86)\SimHub\`).
3. Start SimHub. On the first start SimHub asks whether to enable the new plugin: confirm. The plugin appears as **FFB Belt Tensioner** in the left menu (if not: *Settings → Plugins*, enable *DIY FFB Belt Tensioner* and *Show in left menu*).

## Typical first setup

1. **Firmware tab:** select your COM port and your board, keep *Latest GitHub release*, click **Flash firmware**.
2. **Configuration tab:** power the servo, click **Flash servo parameters**, then power cycle the servo. Enter spindle pitch and working range of your actuator, click **Save to controller** and confirm the restart.
3. **Test tab:** click **Home**, then start a *Hold position* test with a low value, then try *sine* and *brake pulse*. **Stop & park** when done.
4. **SimHub Motion tab:** choose single or dual, select the COM port(s), click **Save SimHub Motion preset...** and import the file in *Motion → Platform Config. → Settings Import and export → Import all platform settings*. Click **Release port for SimHub Motion** and enable motion.

For a dual belt tensioner (one board per actuator) connect to each board in turn: flash it, configure it (every board drives its actuator as axis 1) and test it.

## Building

Requirements: Windows, SimHub installed, Visual Studio 2022 or the .NET SDK (the project targets .NET Framework 4.8 and references the SimHub DLLs from the installation folder).

```powershell
cd SimHubPlugin
dotnet build -c Release
```

* SimHub must be closed: after building, the DLL is copied into the SimHub folder automatically. Disable with `-p:CopyToSimHub=false`.
* SimHub installed somewhere else: `dotnet build -c Release -p:SimHubPath="D:\SimHub\"` (with trailing backslash).
* The output is `SimHubPlugin\bin\Release\DiyBeltTensioner.dll`.

`Resources\esptool.exe` ([esptool](https://github.com/espressif/esptool), GPL-2.0, Windows build) is embedded into the DLL and extracted to `%TEMP%` when flashing. An `esptool.exe` placed next to `DiyBeltTensioner.dll` in the SimHub folder takes precedence.

## Notes

* Downloaded firmware is cached in `%LOCALAPPDATA%\DiyBeltTensioner\firmware`.
* Settings are stored by SimHub in `PluginsData\Common\DiyBeltTensioner.GeneralSettings.json`.
* *Restart controller* uses the `RESTART` serial command. Firmware releases without it are restarted by reopening the port on CP2102 boards; on native USB boards press **RST** or unplug USB.
* Exposed SimHub properties: `BeltTensionerPlugin.Connected`, `.ComPort`, `.FirmwareVersion`, `.EnabledMotors`, `.TestRunning`.
