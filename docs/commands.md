# DIY Belt Tensioner - SimHub Protocol & Command Specification

This document describes all inputs and outputs of the serial communication protocol between the **SimHub DIY Belt Tensioner Plugin** and the controller (ESP32 / Arduino), based on the protocol specification in `BeltTensionner.ino` and `AxisDriver.h`.

---

## 1. Serial Interface Parameters

| Parameter | Value |
| :--- | :--- |
| **Baud Rate** | `250000` bps |
| **Data Bits** | `8` |
| **Parity** | None (`N`) |
| **Stop Bits** | `1` |
| **Flow Control** | None |
| **Line Terminator (ASCII)** | `\r\n` (`0x0D 0x0A` / CR+LF) |

---

## 2. Packet Framing (Binary Protocol)

All binary control commands sent from SimHub to the controller use a fixed frame structure:

```text
+---------------+---------------+---------------+----------------------+---------------+---------------+
| Header Byte 1 | Header Byte 2 |  Command ID   |     Payload Data     | Term Byte 1   | Term Byte 2   |
|     0xFF      |     0xFF      |  (1 Byte)     |    (0 - 8 Bytes)     |     0x0A      |     0x0D      |
+---------------+---------------+---------------+----------------------+---------------+---------------+
```

* **Header:** Always `0xFF 0xFF` (2 Bytes)
* **Command ID:** `uint8_t` command code
* **Payload:** Command-dependent (Big-Endian / MSB first)
* **Terminator:** Always `0x0A 0x0D` (`\n\r` / 2 Bytes)

---

## 3. Input Commands (SimHub $\rightarrow$ Controller)

### CMD 1 (`0x01`): Set Target Position
Transmits the 16-bit target positions for up to 2 motors (Big-Endian).

* **Packet Format (7 Bytes):**
  ```text
  [0xFF] [0xFF] [0x01] [M1_High] [M1_Low] [M2_High] [M2_Low] [0x0A] [0x0D]
  ```
* **Value Range:** `0` to `65535` (Center / 50% tension = approx. `32767`).
* **Conversion in Controller:**
  $$\text{TargetStep} = \text{constrain}\left(\frac{\text{InputValue} \times \text{TotalWorkingRange}}{65535}, 0, \text{TotalWorkingRange}\right)$$
* **Behavior:**
  - Wakes up the motor from standby (`enableMotor`).
  - Drives to the calculated step position via `FastAccelStepper::moveTo()`.
  - Resets the inactivity watchdog timer.
* **Response:** None (fire-and-forget streaming).

---

### CMD 2 (`0x02`): Set Max Speed
Configures the maximum step frequency in Hz (steps per second) for both axes.

* **Packet Format (7 Bytes):**
  ```text
  [0xFF] [0xFF] [0x02] [M1_High] [M1_Low] [M2_High] [M2_Low] [0x0A] [0x0D]
  ```
* **Value Range:** `uint16_t` (`1` to `65535` steps/s).
* **Behavior:** Applies hardware speed scaling via `SPEED_MULTIPLIER` and updates `stepper->setSpeedInHz()`.
* **Response / Log:**
  ```text
  M1 Speed set to <speed>
  M2 Speed set to <speed>
  ```

---

### CMD 3 (`0x03`): Set Max Acceleration
Configures the maximum acceleration in steps/$s^2$ for both axes (as a 32-bit unsigned integer).

* **Packet Format (11 Bytes):**
  ```text
  [0xFF] [0xFF] [0x03] [M1_B3] [M1_B2] [M1_B1] [M1_B0] [M2_B3] [M2_B2] [M2_B1] [M2_B0] [0x0A] [0x0D]
  ```
* **Value Range:** `uint32_t` (`1` to `4294967295` steps/$s^2$).
* **Behavior:** Applies hardware acceleration scaling via `ACCELERATION_MULTIPLIER` and updates `stepper->setAcceleration()`.
* **Response / Log:**
  ```text
  M1 Acceleration set to <accel>
  M2 Acceleration set to <accel>
  ```

---

### CMD 10 (`0x0A`): Query Enabled Motors
Handshake command sent by SimHub during connection setup to determine the number of configured axes.

* **Packet Format (5 Bytes):**
  ```text
  [0xFF] [0xFF] [0x0A] [0x0A] [0x0D]
  ```
* **Response from Controller (ASCII string with CR+LF):**
  ```text
  Enabled motors:<N>
  ```
  *(Example: `Enabled motors:1`)*

---

### CMD 11 (`0x0B`): Dump Sensor Diagnostic
Reads the live sensor/load feedback value of the specified axis.

* **Packet Format (6 Bytes):**
  ```text
  [0xFF] [0xFF] [0x0B] [Axis_ID] [0x0A] [0x0D]
  ```
  *(where `Axis_ID` is 0-indexed: `0` for Motor 1, `1` for Motor 2)*
* **Response from Controller (ASCII string with CR+LF):**
  ```text
  Sensor #<Axis>:<Value>:Trigger level:<Level>:Triggered:<0|1>
  ```
  *(Example: `Sensor #0:45:Trigger level:100:Triggered:0`)*

---

### CMD 12 (`0x0C`): Park Now (Slack Release)
Triggers immediate relaxation of the racing harness and moves the carriage to the idle/park position (10% stroke).

* **Packet Format (5 Bytes):**
  ```text
  [0xFF] [0xFF] [0x0C] [0x0A] [0x0D]
  ```
* **Behavior:** Eases motors to the idle position and shuts down power stages after standstill.
* **Response:** None.

---

### CMD 13 (`0x0D`): Recalibrate / Discard Calibration
Discards the current zero-point calibration and enforces a fresh sensorless homing routine.

* **Packet Format (5 Bytes):**
  ```text
  [0xFF] [0xFF] [0x0D] [0x0A] [0x0D]
  ```
* **Behavior:** Sets `motorReady = false`, launches the sensorless homing routine, and re-zeros the coordinate frame.
* **Response / Log:**
  ```text
  M1 Starting stepper calibration
  ```

---

### CMD 14 (`0x0E`): Query Firmware Version
Sent by SimHub immediately upon connection to verify protocol compatibility.

* **Packet Format (5 Bytes):**
  ```text
  [0xFF] [0xFF] [0x0E] [0x0A] [0x0D]
  ```
* **Response from Controller (ASCII string with CR+LF):**
  ```text
  2.0
  ```
  *(Important: SimHub executes `new Version(response)`. No leading or trailing log lines may surround `"2.0"`!)*

---

### CMD 15 (`0x0F`): Flash Tuned Parameters to Servo EEPROM
Flashes all **305 tuned registers** from `isv57_tunedParameters.h` (Pr0.00 to Pr7.49) to the iSV57 servo and burns them permanently into the servo's internal NVM/EEPROM (`0x019A = 0x5555`).

* **Packet Format (5 Bytes):**
  ```text
  [0xFF] [0xFF] [0x0F] [0x0A] [0x0D]
  ```

---

## 4. Serial ASCII Commands (Serial Monitor)

In addition to SimHub binary framing, the serial interface supports human-readable ASCII commands in any terminal at **250,000 Baud**:

| ASCII Command | Aliases | Description |
| :--- | :--- | :--- |
| **`FLASH_SERVO`** | `FLASH`, `FLASH 1`, `FLASH_SERVO 1` | Verifies and flashes all **305 parameters** from `isv57_tunedParameters.h` into Servo 1 and permanently saves them to EEPROM (`0x5555`). |
| **`FLASH_SERVO 2`** | `FLASH 2` | Flashes all 305 parameters to Servo 2 (dual-actuator setup). |
| **`ENABLE_SERVO`** | `ENABLE`, `ENABLE 1` | Enables the servo power stage (`0x0085 = 0x0383` & `0x0139 = 0x0008`) and energizes the motor immediately. |
| **`ENABLE_SERVO 2`** | `ENABLE 2` | Enables Servo 2. |
| **`DISABLE_SERVO`** | `DISABLE` | Disables the servo power stage (`0x0085 = 0x0303` & `0x0139 = 0x0000`). The motor shaft is free to rotate. |
| **`HOME`** | `CALIBRATE` | Starts automatic sensorless homing and endstop calibration. |
| **`STATUS`** | - | Prints live load current (%), bus voltage (V), and calibration status in SimHub diagnostic format. |
| **`HELP`** | - | Prints an overview of all available serial commands. |

---

## 5. Output Messages (Controller $\rightarrow$ SimHub / PC)

SimHub filters all text lines sent from the controller based on specific conventions:

### 1. Boot Greeting (on startup)
Printed once in `setup()`:
```text
<N> steppers enabled
```
*(e.g. `1 steppers enabled`)*

### 2. Status & Diagnostic Messages (SimHub Log Format)
Every log message intended for the SimHub motion log window **must** begin with `M1 ` or `M2 ` (prefix + space):

| Log Message | Description |
| :--- | :--- |
| `M1 Enabling motor` | Motor coils energized and axis enabled |
| `M1 Disabling motor` | Motor powered down after inactivity timeout |
| `M1 Starting stepper calibration` | Homing / calibration routine initiated |
| `M1 Initial move finished` | Initial clearance move completed |
| `M1 Starting calibration` | Seeking mechanical endstop / sensor |
| `M1 Sensor triggered` | Endstop contact / stall detected |
| `M1 Calibration successful.` | Homing complete, zero point registered |
| `M1 Speed set to <speed>` | New speed parameter applied |
| `M1 Acceleration set to <accel>` | New acceleration parameter applied |
| `M1 Sensor calibration completed` | Axis ready for operation |

---

## 6. Special Operational Modes & Watchdogs

### Inactivity Watchdog (`IDLE_DELAY_MS = 60000 ms`)
- If no motion commands are received for longer than `IDLE_DELAY_MS` (default 60 seconds):
  1. The carriage smoothly travels to the relaxed 10% park position (`moveToIdle`).
  2. Once standstill is reached, motor coils and the iSV57 power stage are completely de-energized (`Disabling motor`).
  3. The RGB Status LED turns **Solid Red**.
- When SimHub sends the next motion command, the axis wakes up, automatically re-homes to guarantee positional accuracy, and resumes live driving.

### Sensor Test Mode (`sensorTestMode = true`)
When enabled in configuration, the controller enters a continuous diagnostic stream:
- Motors remain disabled.
- Streams live telemetry every 500 ms in the format `Sensor #<axis>:<value>:Trigger level:<level>:Triggered:<0|1>`.
