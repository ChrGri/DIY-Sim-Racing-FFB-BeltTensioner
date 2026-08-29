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
* **Visual RGB Status Feedback:** Real-time system feedback via the on-board WS2812 RGB LED (Standby, Homing, Active Driving, Idle Parking, and Error states). See [RGB Status LED Guide](docs/LED.md).
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

## ⚡ Quick Setup Guide

Getting your active FFB Belt Tensioner up and running takes just two steps:

### 1️⃣ Flash the Firmware
Download the latest pre-compiled release for your board and flash it directly within SimHub using the FFB Pedal Dashboard plugin:
👉 **[Firmware Flashing Guide (Step-by-Step with Screenshots)](docs/Firmware_Flashing.md)**

### 2️⃣ Configure the SimHub Motion Plugin
Import the pre-configured preset or configure the controller in SimHub Motion:
👉 **[SimHub Motion Plugin Setup Guide](docs/SimHub_Motion_Setup.md)**

---

## 📚 Documentation & Technical References

| Document | Topic & Content |
| :--- | :--- |
| 💾 **[Firmware Flashing Guide](docs/Firmware_Flashing.md)** | Step-by-step guide to flashing pre-compiled releases using the SimHub FFB Pedal Dashboard USB Flasher. |
| 📖 **[SimHub Motion Setup Guide](docs/SimHub_Motion_Setup.md)** | Step-by-step installation guide with screenshots, axis calibration, and importable `.shmotionoutput` profiles. |
| 🛠️ **[Developer & Hardware Guide](docs/Developer_Guide.md)** | Hardware pinouts, board wiring, `src/Config.h` parameters, and compiling from source with PlatformIO. |
| ⚡ **[Protocol & Command Specification](docs/commands.md)** | Complete specification of binary SimHub packets (`CMD 1`–`15`), serial ASCII commands (`FLASH_SERVO`, `HOME`, `STATUS`), and diagnostic responses. |
| 🏗️ **[Architecture & Execution Reference](docs/reference.md)** | Firmware state machine, sensorless homing workflow, dual-core task design, and step-loss encoder recovery. |
| 💡 **[RGB Status LED Guide](docs/LED.md)** | On-board WS2812 RGB LED color states (Standby, Homing, Active Driving, Parking, Error), pinouts, and brightness tuning. |

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
