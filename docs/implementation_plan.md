# Implementation Plan: ESP32 DIY Simracing Belt Tensioner (iSV57 + Linear Rails)

## Overview
This project builds high-performance firmware for an ESP32-based DIY Simracing Belt Tensioner. The tensioner uses 1 or 2 **Stepperonline iSV57 integrated servo motors** coupled to linear slide rails (Linearschlitten) to which the racing harness is attached.

The firmware communicates directly with the **SimHub BeltTensioner** plugin protocol (250000 baud serial protocol) and features **sensorless homing** using the iSV57's internal Modbus current feedback telemetry (derived from the proven pedal firmware `DiyPedalCode/include/StepperWithLimits.h`).

---

## User Review Required

> [!IMPORTANT]
> **Key Configuration Decisions:**
> 1. **Actuator Count:** The code defaults to supporting either **1** or **2** actuators via `#define NUM_ACTUATORS` in `Config.h`.
> 2. **Modbus UART Topology:** For dual-motor setups, we support dual hardware UARTs (`Serial1` and `Serial2`) so both iSV57 motors can retain their default factory Modbus slave IDs without requiring external register reprogramming.
> 3. **Stepper Acceleration:** We utilize `FastAccelStepper` with ESP32 hardware RMT / MCPWM peripheral timers to ensure jitter-free, ultra-smooth acceleration curves up to 200 kHz pulse rates.

---

## Architecture & System Design

```
+-------------------------------------------------------------+
|                      SimHub / Host PC                       |
|                 BeltTensioner Serial Plugin                 |
+------------------------------+------------------------------+
                               | Binary Serial (250000 Baud)
                               v
+-------------------------------------------------------------+
|                     ESP32 Main Controller                   |
|  - Serial Packet Parser (0xFF 0xFF <cmd> ... 0x0A 0x0D)     |
|  - Position Mapping (0..65535 -> 0..workingRange steps)     |
|  - Idle Timeout Watchdog & Belt Slack Release / Park        |
+-------------------+--------------------+--------------------+
                    |                    |
         +----------v----------+ +-------v-----------+
         |     Actuator 1      | |    Actuator 2     |
         |    (e.g. Left)      | |   (e.g. Right)    |
         +----------+----------+ +-------+-----------+
                    |                    |
    +---------------+----+          +----+---------------+
    |                    |          |                    |
    v                    v          v                    v
FastAccelStepper      Modbus     FastAccelStepper     Modbus
(Pulse / Dir Pins)  (Serial1)   (Pulse / Dir Pins)  (Serial2)
    |                    |          |                    |
    +--------+-----------+          +--------+-----------+
             |                               |
             v                               v
    iSV57 Servo #1 + Rail           iSV57 Servo #2 + Rail
```

---

## Proposed Changes

### New Project Directory: `src/` and Configuration Files

#### [NEW] [platformio.ini](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/platformio.ini)
- PlatformIO project definition supporting standard ESP32 (WROOM/DevKit) and ESP32-S3.
- Includes `gin66/FastAccelStepper` and necessary FreeRTOS / Arduino frameworks.

#### [NEW] [Config.h](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/Config.h)
- Central user-friendly configuration:
  - `NUM_ACTUATORS`: `1` or `2`
  - Pin assignments for Stepper 1 & 2 (STEP, DIR, ENA, UART RX/TX, ALM SRDY)
  - Mechanical parameters: Lead screw pitch (mm/rev), microsteps (e.g. 3200 steps/rev), rail travel range (mm)
  - Sensorless homing: Current spike threshold (%), homing search speed, backoff distance
  - Belt tensioner behavior: Center vs. zero park position, idle delay (ms), smooth ramp-in time

#### [NEW] [Modbus.h](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/Modbus.h) & [Modbus.cpp](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/Modbus.cpp)
- High-efficiency Modbus RTU protocol engine supporting CRC16, Read Holding Registers (FC03), Write Single Register (FC06), and Write Multiple Registers (FC16).
- Supports independent `HardwareSerial` instances for each axis.

#### [NEW] [isv57communication.h](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/isv57communication.h) & [isv57communication.cpp](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/isv57communication.cpp)
- iSV57 integrated servo communication module:
  - Modbus Slave ID auto-discovery & heartbeat lifeline check.
  - Cyclic telemetry readout (servo position, current %, tracking error, DC bus voltage).
  - Servo parameterization (microsteps, electronic gear ratio, bleeder braking voltage, stiffness).
  - Clear alarms, enable/disable axis, and zero internal encoder.

#### [NEW] [isv57_tunedParameters.h](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/isv57_tunedParameters.h)
- Optimal factory / tuned register table for iSV57 to ensure responsive, high-torque position tracking without oscillatory ringing.

#### [NEW] [AxisUnit.h](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/AxisUnit.h) & [AxisUnit.cpp](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/AxisUnit.cpp)
- Encapsulates single actuator unit (Motor + Linear Rail):
  - **Sensorless Homing State Machine:**
    1. Verify servo voltage & communication lifeline.
    2. Move backwards towards physical hard stop at safe homing speed.
    3. Monitor `abs(current_percent) > threshold`.
    4. Upon stall detection, `forceStop()`, record minimum limit, back off slightly, and zero coordinates.
    5. (Optional) Sweep forward to verify max stroke length.
    6. Transition to `HOMED_READY` and move to idle position.
  - **Dynamic Motion & Limits:** Smooth target position updates, velocity/acceleration scaling, and soft-stop protection.

#### [NEW] [BeltTensionerProtocol.h](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/BeltTensionerProtocol.h) & [BeltTensionerProtocol.cpp](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/BeltTensionerProtocol.cpp)
- Complete parser and dispatcher for the SimHub BeltTensioner protocol:
  - `CMD 1`: 16-bit target position (`val1`, `val2`) -> maps 0..65535 to physical steps
  - `CMD 2`: 16-bit max speed
  - `CMD 3`: 32-bit max acceleration
  - `CMD 10`: Query enabled motors (`Enabled motors: X`)
  - `CMD 11`: Dump sensor diagnostic data
  - `CMD 12`: Park now (move to idle position)
  - `CMD 13`: Trigger sensorless recalibration
  - `CMD 14`: Firmware version string (`2.0`)

#### [NEW] [main.cpp](file:///c:/Users/chris/OneDrive/Desktop/GIT/DiyFfbBeltTensioner/src/main.cpp)
- Main firmware orchestration:
  - Setup serial interfaces (Host PC USB Serial @ 250000 baud + Motor UARTs @ 38400 baud).
  - Launch FreeRTOS background task on Core 0 for cyclic Modbus telemetry & stall monitoring.
  - Core 1 execution for SimHub serial packet processing and motion commands.
  - Idle watchdog: parks motors when no SimHub game data is received for `idleDelay` ms.

---

## Verification Plan

### Automated Build & Syntax Checks
- Run `pio run` (or syntax verification) to ensure clean compilation across target ESP32 environments.

### Protocol Verification
- Verify binary packet decoding against all SimHub commands (positions, speeds, accelerations, queries).

### Sensorless Homing Verification
- Verify endstop detection threshold logic, backoff step calculation, and zero coordinate registration.
