# SimHub Plugin: DIY FFB Belt Tensioner

A SimHub plugin to set up the belt tensioner without a serial terminal. It shows up as **FFB Belt Tensioner** in the left SimHub menu and uses the look of the DIY FFB Pedal plugin.

The plugin works with **channels**: a single channel belt tensioner (one actuator for both shoulder belts) or a dual channel one (one actuator per shoulder belt). Every channel is one controller board with one actuator on its own COM port, and all channels are connected at the same time.

| Tab | What it does |
| :--- | :--- |
| **SETUP** | Pick *Single channel* or *Dual channel*. The rendering of the layout is shown with one card per belt (left / right for dual): COM port and connection, status (servo state, load, voltage, position), spindle pitch and working range with **Auto-detect range** (homing to both end stops), Home / Park / Restart. Below: **Save to controller** for all channels and the **SimHub Motion** preset (limits, direction, *Create SimHub Motion preset*, *Release ports for SimHub Motion*). |
| **CONFIG** | Channel switcher (LEFT / RIGHT or BELT) like Throttle / Brake / Clutch in the pedal plugin. Stroke view with safety window, park position, carriage position and test position, then the tabs **Positions & Direction**, **Idle & Homing**, **Motion Test** (hold / sine / brake pulse) and **Servo** (flash / check the iSV57 parameters, servo register editor). Actions: Home, Park, Restart, Read, Web Graphs. |
| **FIRMWARE** | Flashes the controllers via USB with the embedded `esptool`, one row per channel with its board variant. The firmware is the latest [GitHub release](https://github.com/ChrGri/DIY-Sim-Racing-FFB-BeltTensioner/releases) (board variants of that release) or a local release `.zip` / build folder. Native USB boards (1200 bps touch, new bootloader COM port) and CP2102 boards (DTR/RTS reset) are handled automatically. |
| **SYSTEM** | Plugin settings (auto-connect, live status, plugin updates), COM port identification, status messages and the serial log / console of every channel. |

**COM port detection:** the port lists show what is connected, e.g. `COM19  (Belt Tensioner)` or `COM31  (FFB Pedal: Throttle)`. Native USB boards are recognized by their USB product name (`diy_belt_tensioner`) or their USB IDs (pedals: VID `303A` with PID `8332` clutch, `8333` brake, `8334` throttle, `8331` bridge, product `FFB_Pedal_<role>`). CP2102 boards all look the same on USB: SYSTEM → **Identify USB-UART boards** opens them briefly and waits for the belt tensioner greeting (this resets the ESP32 behind the port). Detected belt tensioners are assigned to channels without a port automatically.

**One actuator per COM port:** the plugin always drives axis 1 of a controller. Build the firmware with `NUM_ACTUATORS 1`; the plugin warns when a controller reports two actuators.

**Settings:** spindle pitch and working range need firmware with `SET_PITCH` / `SET_RANGE`; direction, positions, idle and homing need firmware with the `SETTINGS` / `SET` commands, auto-detect of the range `MEASURE_RANGE`, the servo status CMD 16 (see [docs/commands.md](../docs/commands.md)). With older firmware these controls stay disabled or show less.

**Servo register editor:** reading works with every firmware (`DUMP_SERVO`); editing and saving single registers needs firmware with `SERVO_WRITE` / `SERVO_SAVE`.

**Web Graphs:** CONFIG → *Web Graphs* switches the live graph web page of the controller on if needed (`WEBGRAPHS ON`), waits for its WiFi and opens the page. Without a saved WiFi the controller opens the access point `diy_belt_tensioner`; connect the PC or a phone to it first.

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

1. **SETUP:** pick single or dual channel and select the COM port of every belt (the lists show which port is a belt tensioner), **Connect all**.
2. **FIRMWARE:** check the board variant of every channel, click **Flash**. The plugin reconnects afterwards.
3. **CONFIG → Servo:** power the servo, **Flash servo parameters**, power cycle the servo. Repeat for the other channel.
4. **SETUP:** set the spindle pitch, click **Auto-detect range** (or enter the working range), **Save to controller** and confirm the restart.
5. **CONFIG:** check direction and limits in the stroke view, then **Motion Test** with low values.
6. **SETUP → SimHub Motion:** **Create SimHub Motion preset...**, import it in SimHub (*Motion → Platform Config. → Settings Import and export → Import all platform settings*), **Release ports for SimHub Motion** and enable motion.

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
* Exposed SimHub properties per channel (`Single`, `Left`, `Right`): `BeltTensionerPlugin.Left.Connected`, `.ComPort`, `.Status`, `.ServoLoad`, `.TestRunning`.
