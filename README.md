<p align="center">
  <img src="docs/media/banner_variant_C.png" alt="DIY Sim Racing FFB Belt Tensioner Banner" width="100%">
</p>

# DIY Sim Racing Active FFB Belt Tensioner

[![CC BY-NC-SA 4.0][cc-by-nc-sa-shield]][cc-by-nc-sa]
[![PlatformIO](https://img.shields.io/badge/PlatformIO-Build-orange.svg)](https://platformio.org/)
[![SimHub Compatible](https://img.shields.io/badge/SimHub-Plugin%20Compatible-blue.svg)](https://www.simhubdash.com/)
[![Support on Ko-fi](https://img.shields.io/badge/Ko--fi-Donate-ff5e5b.svg?logo=kofi&logoColor=white)](https://ko-fi.com/captainchris88)
[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20A%20Coffee-Donate-yellow.svg?logo=buy-me-a-coffee)](https://buymeacoffee.com/captainchris)

This repository contains the firmware and hardware integration documentation for an **active Force Feedback (FFB) Seatbelt Tensioner** for sim racing rigs. 

It is designed to **reuse the electronics, controller boards, and mechanical components from the [DIY Sim Racing FFB Pedal project](https://github.com/ChrGri/DIY-Sim-Racing-FFB-Pedal)** while being **100% plug & play compatible with the official [SimHub Belt Tensioner Motion Plugin](https://www.simhubdash.com/diy-belt-tensionner/)**.

---

## 🎬 Demonstration in Action

https://github.com/user-attachments/assets/4027d6b4-1a5e-4edc-9e6e-c9eee0924ece

---

## 🚀 Key Highlights

* **Reuse Existing DIY FFB Pedal Hardware:** Designed to directly utilize the same **ControlBoard_V6 / V7 (ESP32-S3)** and **Stepperonline iSV57 integrated servo motors** used in the DIY FFB Pedal project.
* **100% SimHub Motion Plugin Compatible:** Communicates via the standard binary SimHub Belt Tensioner protocol (v2.0) at 250,000 baud with no custom SimHub forks required.
* **Sensorless Homing via Modbus Telemetry:** Automatic, physical zero-point calibration using real-time Modbus current feedback from the iSV57 servo without needing fragile external microswitches or optical sensors.
* **Dynamic Speed & Acceleration Scaling:** Built-in hardware multipliers scale SimHub's UI slider limits (36,000 Hz / 100,000 steps/s²) to the full servo potential (**180,000 Hz** pulse frequency and **600,000 steps/s²** acceleration) for instantaneous, snappy braking belt pull.
* **Inactivity Watchdog & Auto-Park:** Automatically eases the harness to a relaxed park position and unpowers the coils after 5 seconds of inactivity.
* **Dual-Core FreeRTOS Architecture:**
  * **Core 0:** Cyclic Modbus RS485/RS232 telemetry, voltage monitoring, and continuous stall detection.
  * **Core 1:** SimHub binary serial stream decoding and hardware pulse generation using `FastAccelStepper`.

---

## 🛠️ Reusing DIY FFB Pedal Hardware

If you have already built or gathered parts for the [DIY Sim Racing FFB Pedal](https://github.com/ChrGri/DIY-Sim-Racing-FFB-Pedal), you can directly build this belt tensioner using the same ecosystem:

| Component | DIY FFB Pedal Equivalent | Role in Belt Tensioner |
| :--- | :--- | :--- |
| **Controller PCB** | `ControlBoard_V6` (ESP32-S3 DevKit) or `ControlBoard_V7` (ESP32-S3 Zero) | Microcontroller running the motion generator & SimHub protocol. |
| **Actuator** | Stepperonline iSV57T-180 / iSV57-130 integrated servo | High-torque closed-loop servo driving the linear belt puller. |
| **Linear Rail** | 100mm / 150mm linear rail with ball/lead screw (e.g. 10mm pitch) | Converts rotary torque into fast linear harness pull. |
| **Power Supply** | 36V / 48V DC Power Supply (MeanWell or similar) | Powers the iSV57 integrated servo and controller. |
| **RS232 / Modbus** | Onboard RS232 transceiver on ControlBoard | Reads live current and encoder telemetry from the servo. |

---

## 🔌 Hardware Setup & Pinout

### ControlBoard_V6 (ESP32-S3 DevKit)

| Function | ESP32-S3 Pin | Note |
| :--- | :--- | :--- |
| **Actuator 1 STEP (PUL+)** | `GPIO 37` | Hardware pulse output |
| **Actuator 1 DIR (DIR+)** | `GPIO 36` | Direction signal (`INVERT_DIR = true`) |
| **Actuator 1 Modbus RX** | `GPIO 1` | RS232 Receive from iSV57 |
| **Actuator 1 Modbus TX** | `GPIO 2` | RS232 Transmit to iSV57 |
| **Actuator 1 Alarm (ALM)** | `GPIO 11` | Servo fault detection |
| **Actuator 1 Brake Resistor** | `GPIO 35` | Optional brake chopper control |
| **USB Serial** | `CP2102 UART0` | SimHub connection (`250000` baud) |

### ControlBoard_V7 (ESP32-S3-Zero)

| Function | ESP32-S3 Pin | Note |
| :--- | :--- | :--- |
| **Actuator 1 STEP (PUL+)** | `GPIO 4` | Hardware pulse output |
| **Actuator 1 DIR (DIR+)** | `GPIO 5` | Direction signal |
| **Actuator 1 Modbus RX** | `GPIO 3` | RS232 Receive |
| **Actuator 1 Modbus TX** | `GPIO 2` | RS232 Transmit |

---

## ⚙️ Configuration (`src/Config.h`)

All user-configurable parameters can be adjusted in [src/Config.h](src/Config.h):

```c
// Stroke length and motor mechanics
#define NUM_ACTUATORS                 1       // 1 = Mono tensioner, 2 = Dual (Left + Right)
#define STEPS_PER_MOTOR_REV           3200U   // iSV57 microsteps per revolution
#define SPINDLE_PITCH_MM              10.0f   // Lead screw pitch (mm per rev)
#define TOTAL_WORKING_RANGE_MM        50.0f   // Usable stroke distance on rail (e.g. 50mm)

// Dynamic Multipliers for SimHub UI Sliders
#define SPEED_MULTIPLIER              5.0f    // Scales 36k Hz -> 180k Hz
#define ACCELERATION_MULTIPLIER       6.0f    // Scales 100k -> 600k steps/s^2

// Tension Direction (false = 0% loose / 100% pull tight on braking)
#define INVERT_TENSION_DIRECTION      false

// Sensorless Homing (false = single-endstop homing, true = dual-endstop stroke measurement)
#define MEASURE_MAX_TRAVEL_SENSORLESS false
#define HOMING_CURRENT_THRESHOLD_PCT  15      // Servo current % threshold for endstop contact
```

---

## 💻 Flashing the Firmware

### Using PlatformIO (VS Code)

1. Clone this repository:
   ```bash
   git clone https://github.com/ChrGri/DiyFfbBeltTensioner.git
   ```
2. Open the cloned folder in **VS Code** with the **PlatformIO IDE** extension installed.
3. Select your target environment:
   * `ControlBoard_V6` for ESP32-S3 DevKit
   * `ControlBoard_V7` for ESP32-S3-Zero
4. Connect the board via USB and click **Upload** (or run):
   ```powershell
   pio run -e ControlBoard_V6 -t upload
   ```

---

## 🎮 SimHub Configuration

A detailed visual setup guide with screenshots and pre-configured import profiles is available in the documentation:

👉 **[SimHub Motion Plugin Setup Guide (Step-by-Step)](docs/SimHub_Motion_Setup.md)**

### Quick Setup Overview:
1. In **SimHub**, navigate to **Motion** $\rightarrow$ **Platform Config.**
2. Either **Import** the pre-configured preset from [`docs/motionPluginSetup/AllPlatformSettings.shmotionoutput`](docs/motionPluginSetup/AllPlatformSettings.shmotionoutput) or add a new **SimHub DIY Belt Tensioner** controller.
3. Set your ESP32 serial port (`250000` baud), set speed & acceleration sliders to maximum (`36000` / `100000`), and disable *Rotary lever compensation*.
4. Click **Enable Motion** $\rightarrow$ The carriage will automatically home against the physical stop, back off into the relaxed park position, and instantly respond to in-game telemetry!

---

## ⚠️ Disclaimer & Safety

> [!WARNING]
> **Active force feedback seatbelt tensioners and servos produce high mechanical forces and rapid motions.**
> - Keep hands, loose clothing, and cables clear of linear rails, pulleys, and belt loops during operation.
> - Ensure your rig structure is rigid and securely mounted.
> - Always include a readily accessible emergency power cut-off / kill switch for high-voltage power supplies.
> - Use this project responsibly and at your own risk.

---

## 💖 Supporting the Project

If this project helps bring your sim racing rig to life, consider supporting further open-source development:

[![Support on Ko-fi](https://img.shields.io/badge/Ko--fi-Donate-ff5e5b.svg?logo=kofi&logoColor=white&style=for-the-badge)](https://ko-fi.com/captainchris88)
[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20A%20Coffee-Donate-yellow.svg?logo=buy-me-a-coffee&style=for-the-badge)](https://buymeacoffee.com/captainchris)

---

## 🌟 Star History

[![Star History Chart](https://api.star-history.com/svg?repos=ChrGri/DiyFfbBeltTensioner&type=Date)](https://star-history.com/#ChrGri/DiyFfbBeltTensioner&Date)

---

## 📄 License

This work is licensed under a [Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International License (CC BY-NC-SA 4.0)][cc-by-nc-sa].

[![CC BY-NC-SA 4.0][cc-by-nc-sa-image]][cc-by-nc-sa]

[cc-by-nc-sa]: http://creativecommons.org/licenses/by-nc-sa/4.0/
[cc-by-nc-sa-image]: https://licensebuttons.net/l/by-nc-sa/4.0/88x31.png
[cc-by-nc-sa-shield]: https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey.svg
