# Walkthrough: ESP32 DIY Simracing Belt Tensioner (iSV57 + Linear Rails)

We have created the complete ESP32 firmware for the DIY Belt Tensioner in `src/`, fully compatible with the SimHub BeltTensioner plugin protocol and featuring sensorless homing using Stepperonline iSV57 integrated servo telemetry.

---

## Architecture & Features

### 1. SimHub BeltTensioner Protocol Compatibility
- **Baud Rate:** `250000` baud.
- **Header:** `0xFF 0xFF` packet detection.
- **Commands:**
  - `CMD 1`: 16-bit target position (`val1`, `val2`) mapped to physical working range steps.
  - `CMD 2`: 16-bit speed limits.
  - `CMD 3`: 32-bit acceleration limits.
  - `CMD 10`: Query enabled motors count (`Enabled motors: X`).
  - `CMD 11`: Sensor diagnostics dump (reports current load % & trigger state).
  - `CMD 12`: Park now (moves to slack/idle position).
  - `CMD 13`: Trigger sensorless recalibration.
  - `CMD 14`: Firmware version string query (returns `"2.0"`).
- **Idle Watchdog:** Automatically moves motors to the idle / slack position after 5 seconds of inactivity to avoid motor strain.

### 2. Sensorless Homing via iSV57 Modbus
- Uses Modbus RTU at 38400 baud over hardware UART (`Serial2` / `Serial1`).
- Monitors cyclic register `0x0081` (Servo Current Feedback %).
- Automatically sweeps backward towards the linear rail endstop at a safe, controlled speed.
- Detects mechanical hard stop when current exceeds `HOMING_CURRENT_THRESHOLD_PCT` (default 30%).
- Immediately executes `forceStop()`, backs off slightly by `HOMING_BACKOFF_STEPS` (500 steps) to prevent mechanical binding, and zeros both the ESP coordinate frame and the iSV57 internal encoder.

### 3. Dual-Core FreeRTOS Performance
- **Core 0 (`modbusTelemetryTask`):** Cyclic Modbus telemetry polling & stall detection.
- **Core 1 (`loop`):** High-speed SimHub packet parsing and motion dispatch.
- **Hardware Step Generation:** Driven by `FastAccelStepper` utilizing ESP32 hardware RMT / MCPWM peripheral timers for jitter-free pulses up to >100 kHz.

---

## Created Files in `src/`

| File | Purpose |
|---|---|
| [Config.h](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/Config.h) | Central configuration (1 or 2 actuators, pin assignments, spindle pitch, travel range, homing thresholds). |
| [Modbus.h](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/Modbus.h) & [Modbus.cpp](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/Modbus.cpp) | Modbus RTU communication engine (CRC-16, FC03 read, FC06 write, FC16 batch write). |
| [isv57communication.h](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/isv57communication.h) & [isv57communication.cpp](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/isv57communication.cpp) | Driver for Stepperonline iSV57 integrated servo (slave ID discovery, telemetry, zeroing, alarm clearing). |
| [isv57_tunedParameters.h](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/isv57_tunedParameters.h) | Pre-tuned parameter table for stiff, responsive servo control without overshoot. |
| [AxisUnit.h](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/AxisUnit.h) & [AxisUnit.cpp](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/AxisUnit.cpp) | Encapsulates one actuator (motor + linear rail), sensorless homing state machine, and soft limits. |
| [BeltTensionerProtocol.h](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/BeltTensionerProtocol.h) & [BeltTensionerProtocol.cpp](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/BeltTensionerProtocol.cpp) | Binary serial protocol parser and dispatcher for SimHub. |
| [main.cpp](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/main.cpp) | System setup, FreeRTOS dual-core tasks, and main loop. |
| [platformio.ini](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/platformio.ini) | PlatformIO build configuration supporting `esp32dev` and `esp32-s3-devkitc-1`. |

---

## Verification & Compilation Results

Both target build environments were compiled and validated with PlatformIO:

```bash
Environment    Status    Duration
-------------  --------  ------------
esp32_devkit   SUCCESS   00:00:53.758
esp32_s3       SUCCESS   00:00:50.224
```

---

## Wiring & Configuration Quick Guide

In [Config.h](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/Config.h), you can adjust:
- **Actuator count:** `#define NUM_ACTUATORS 2` (or `1` for single belt)
- **Actuator 1 (Left Belt):**
  - `STEP_PIN`: `18`
  - `DIR_PIN`: `19`
  - `MODBUS_RX`: `16`
  - `MODBUS_TX`: `17`
- **Actuator 2 (Right Belt):**
  - `STEP_PIN`: `22`
  - `DIR_PIN`: `23`
  - `MODBUS_RX`: `26`
  - `MODBUS_TX`: `27`
- **Mechanical:** `SPINDLE_PITCH_MM` (e.g. 5mm) and `TOTAL_WORKING_RANGE_MM` (e.g. 100mm).
