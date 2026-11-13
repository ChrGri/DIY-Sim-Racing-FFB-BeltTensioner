# SimHub Plugin: DIY FFB Belt Tensioner

A SimHub plugin to set up the belt tensioner without a serial terminal. It shows up as **FFB Belt Tensioner** in the left SimHub menu and uses the look of the DIY FFB Pedal plugin:

| Tab | What it does |
| :--- | :--- |
| **TENSIONER** | Left: info, COM port and connection, plugin settings, action buttons (Restart, Home, Park, Status, Read, Serial Logs) and status. Right: the **STROKE** view (working range, safety window, park position, braking direction and the live position of a test) or the **MOTION TEST** (hold / sine / brake pulse per axis). Below: settings in the tabs **General** (spindle pitch, working range per axis, tension direction, center after homing), **Positions** (park position, min. / max. limit), **Idle & Homing** (idle delay, homing load threshold, back-off, home after power on) and **Servo** (flash / check the iSV57 parameters, **servo register editor**: all 305 registers next to the tuned values, editable, save to EEPROM, CSV export / import). Changes are written with **Save to controller** and applied after a restart. |
| **FIRMWARE** | Flashes the controller via USB with the embedded `esptool`. The firmware is downloaded from the latest [GitHub release](https://github.com/ChrGri/DIY-Sim-Racing-FFB-BeltTensioner/releases) for the selected board, or taken from a local release `.zip` / build folder. Native USB boards (1200 bps touch, new bootloader COM port) and CP2102 boards (DTR/RTS reset) are handled automatically. |
| **MOTION** | Creates a ready-to-import `.shmotionoutput` preset (single or dual belt tensioner) with your COM port(s), direction and speed / acceleration limits, based on the presets in [`docs/motionPluginSetup`](../docs/motionPluginSetup/). |
| **SERIAL LOGS** | Log and console for any ASCII command (`HELP`, `SETTINGS`, `STATUS`, `DUMP_SERVO`, ...). |

**COM port detection:** the port list shows what is connected, e.g. `COM19  (Belt Tensioner)` or `COM31  (FFB Pedal)`. Native USB boards are recognized by their USB product name (`diy_belt_tensioner`, `DiyFfbPedal`) or the pedal's VID/PID `F011:F011`. CP2102 boards all look the same on USB: **Identify USB-UART boards** opens them briefly and waits for the belt tensioner greeting (this resets the ESP32 behind the port).

**Servo register editor:** reading works with every firmware (`DUMP_SERVO`); editing and saving single registers needs firmware with `SERVO_WRITE` / `SERVO_SAVE`.

**Settings:** spindle pitch and working range need firmware with `SET_PITCH` / `SET_RANGE`; direction, positions, idle and homing need firmware with the `SETTINGS` / `SET` commands (see [docs/commands.md](../docs/commands.md)). With older firmware these controls stay disabled.

> [!IMPORTANT]
> A COM port can only be used by one program. The plugin and SimHub Motion therefore never use the controller at the same time: disable SimHub Motion before you connect in the plugin, and click **Disconnect** / **Release port for SimHub Motion** before you enable SimHub Motion again.

## Installation

1. Download `DiyBeltTensioner.dll` from the latest [release](https://github.com/ChrGri/DIY-Sim-Racing-FFB-BeltTensioner/releases) (the release workflow builds it together with the firmware).
2. Close SimHub and copy the DLL into the SimHub installation folder (usually `C:\Program Files (x86)\SimHub\`).
3. Start SimHub. On the first start SimHub asks whether to enable the new plugin: confirm. The plugin appears as **FFB Belt Tensioner** in the left menu (if not: *Settings → Plugins*, enable *DIY FFB Belt Tensioner* and *Show in left menu*).

## Updates

* **Plugin:** at start the plugin checks the releases (switch *Check for plugin updates*, button *Check now*). If a newer `DiyBeltTensioner.dll` is available, a banner offers **Update now**: the DLL is downloaded, SimHub closes, the plugin is replaced (admin prompt, the old DLL is kept as `DiyBeltTensioner.dll.bak`) and SimHub starts again.
* **Firmware:** FIRMWARE tab → the latest release is used; *Board* lists the board variants it contains. Pick your board, then **Flash firmware**.

## Typical first setup

1. **TENSIONER:** select the COM port of the controller (the list shows which port is the belt tensioner).
2. **FIRMWARE:** select your board, keep *Latest GitHub release*, click **Flash firmware**. The plugin reconnects afterwards.
3. **TENSIONER → Servo:** power the servo, click **Flash servo parameters**, then power cycle the servo.
4. **TENSIONER → General / Positions / Idle & Homing:** enter spindle pitch and working range, check direction and limits on the STROKE view, click **Save to controller** and confirm the restart.
5. **TENSIONER → MOTION TEST:** click **Home**, then start a *Hold position* test with a low value, then try *Sine* and *Brake pulse*. **Stop & park** when done.
6. **MOTION:** choose single or dual, check the COM port(s), click **Save SimHub Motion preset...** and import the file in *Motion → Platform Config. → Settings Import and export → Import all platform settings*. Click **Release port for SimHub Motion** and enable motion.

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
