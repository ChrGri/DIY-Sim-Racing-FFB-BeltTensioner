# Developer & Hardware Configuration Guide

This guide covers hardware pinouts, board configurations, firmware parameters in `src/Config.h`, and building/compiling the firmware from source using PlatformIO.

---

## 🔌 Hardware Setup & Pinouts

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

## ⚙️ Firmware Configuration (`src/Config.h`)

All user-configurable mechanical, motion scaling, and safety parameters can be customized in [`src/Config.h`](../src/Config.h):

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

## 💻 Compiling & Flashing from Source (PlatformIO)

If you want to modify code, add custom features, or build locally:

### 1. Prerequisites
* [VS Code](https://code.visualstudio.com/)
* [PlatformIO IDE Extension](https://platformio.org/install/ide?install=vscode)

### 2. Clone the Repository
```bash
git clone https://github.com/ChrGri/DIY-Sim-Racing-FFB-BeltTensioner.git
```

### 3. Open and Build in PlatformIO
1. Open the cloned folder in **VS Code**.
2. Select your environment from the PlatformIO status bar:
   * `ControlBoard_V6` for ESP32-S3 DevKit
   * `ControlBoard_V7` for ESP32-S3-Zero
   * `esp32_devkit` for ESP32 DevKit v1
3. Connect your board via USB and click **Upload** (or run):
   ```powershell
   pio run -e ControlBoard_V6 -t upload
   ```
