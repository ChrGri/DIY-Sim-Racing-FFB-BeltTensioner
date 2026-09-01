# 🛠️ Control PCB Assembly & Soldering Guide (Belt Tensioner)

This guide walks you through assembling and soldering the controller PCB (`ControlBoard_V7` with ESP32-S3 Zero or `ControlBoard_V6` with ESP32-S3 DevKit) specifically tailored for the **DIY Active FFB Belt Tensioner**.

---

## ⚡ Quick Summary: How the Belt Tensioner Differs from the Pedal

Because the Belt Tensioner operates via position control and sensorless Modbus telemetry instead of a load cell, **more than half of the components from the pedal PCB are omitted**. This makes assembling the board much faster and easier!

| ✅ Required & Populated (Belt Tensioner) | ❌ Omitted & Skipped (Pedal Only) |
| :--- | :--- |
| **SR5100 Schottky Diode** (Power input protection) | **ADS1220 ADC Chip** (Load cell amplifier) |
| **XT30 Power Connector** (Angled male DC input) | **RC Filter Network** (100Ω resistors & 0.1µF caps) |
| **SP2323 / SP3232 RS232 Transceiver** | **FR120N MOSFET** (Brake chopper circuit) |
| **RS232 Charge-Pump Caps** (0.1 µF / 104) | **8Ω / 10Ω 5W Brake Resistor** (Load dump) |
| **ESP32-S3 Zero Board** (Microcontroller) | **Load Cell Screw Terminals** |
| **Pulse / Dir / Alm Servo Interface Headers** | |
| **Modbus Telemetry Port** (5-pin debug interface) | |

> [!TIP]
> Base Soldering Guide: This guide builds upon the original pedal documentation:  
> 👉 **[DIY FFB Pedal Soldering Guide (Soldering_V7)](https://github.com/ChrGri/DIY-Sim-Racing-FFB-Pedal-Mechanical-Design/tree/main/Soldering_V7)**

---

> ℹ️ **Affiliate Disclosure:**  
> Some links in this document are affiliate links (marked with an asterisk `*`). When you purchase through these links, a small commission may be paid to help support open-source development and hardware prototyping—at no additional cost to you. Thank you for your support!

---

## 💡 Soldering Tips & Recommended Tools

A good soldering iron makes your life easier:
* I like the portable **[TS80*](https://s.click.aliexpress.com/e/_c33CYe8n)** and **[TS101*](https://s.click.aliexpress.com/e/_opJpOtW)** soldering irons.
* Helpful soldering tutorial video: 📺 **[Soldering tips can be found here](https://www.youtube.com/watch?v=DfC5FBsud7o)**.

---

## 📋 Required Components for Assembly

For direct purchasing links, see the **[Bill of Materials (BOM)](BOM.md)**.

| Component | Role in Belt Tensioner |
| :--- | :--- |
| **Control PCB** | `ControlBoard_V7` (ESP32-S3 Zero) or `ControlBoard_V6` (ESP32-S3 DevKit) |
| **ESP32-S3** | Microcontroller running the motion engine and SimHub binary protocol |
| **SP2323 / SP3232** | RS232 transceiver IC for Modbus communication with the iSV57 servo |
| **0.1 µF Capacitors (104)** | Charge-pump capacitors for the RS232 transceiver |
| **SR5100 Schottky Diode** | Reverse-polarity and inductive flyback protection on DC power input |
| **XT30 Connector (Angled)** | High-current DC power input connector (male, angled) |
| **2.54 mm Screw Terminals / Pins** | Connections for `PUL+`, `DIR+`, `ALM`, and Modbus `TX`/`RX` |
| **AWG 18 Wire** | DC power lines between PSU, PCB, and iSV57 servo |
| **AWG 30 Wire** | Signal and logic wiring |

---

## 🔧 Step-by-Step Soldering Instructions

### Step 1: Power Input Circuit (XT30 & SR5100 Diode)
1. **SR5100 Diode:** Solder the SR5100 Schottky diode. Pay close attention to orientation: the white/silver ring on the diode body corresponds to the cathode line on the PCB silkscreen.
2. **XT30 Connector:** Solder the angled male XT30 connector into the power input pads. Ensure positive (`+`) and negative (`-`) align with the silkscreen.
3. ❌ **Do NOT install the MOSFET or Brake Resistor:** Unlike the pedal, the belt tensioner does not generate significant regenerative kickback during normal operation. Leave the FR120N MOSFET and power resistor pads unpopulated.

### Step 2: RS232 Modbus Transceiver (SP2323 / SP3232)
The RS232 transceiver enables the ESP32 to query the iSV57 internal drive for real-time current, encoder position, and error codes:
1. Align the notch or dot on the SP2323/SP3232 IC with Pin 1 on the PCB silkscreen.
2. Solder the IC pins.
3. Populate the required charge-pump decoupling capacitors (`0.1 µF / 104`) adjacent to the transceiver IC.

### Step 3: Skip the Load Cell Circuit (ADS1220)
* ❌ **Do NOT solder the ADS1220 ADC chip.**
* ❌ **Do NOT solder the RC filter resistors or capacitors.**
* The belt tensioner relies on **sensorless stall homing** and encoder feedback; physical load cell measurement is not used.

### Step 4: ESP32-S3 Microcontroller
* **ControlBoard_V7:** Solder the female header strips or solder the **Waveshare ESP32-S3 Zero** directly to the PCB pads. Ensure the USB-C port faces outward for easy firmware flashing.
* **ControlBoard_V6:** Solder female pin headers for the **ESP32-S3 DevKitC-1** board.

### Step 5: Wiring the iSV57 Integrated Servo Motor

You only need two wiring interfaces between the PCB and the iSV57 motor:

#### A. Motion Control Signals (Screw Terminals / Pin Header)
| Board Signal | Target Pin on iSV57 Servo | Description |
| :--- | :--- | :--- |
| **PUL+ / STEP** | `PUL+` (Pulse) | High-speed motion step pulses |
| **DIR+ / DIR**  | `DIR+` (Direction) | Motion direction signal |
| **ALM / FAULT** | `ALM+` (Alarm) | Servo error / trip detection |
| **GND / COM**   | `PUL-` / `DIR-` / `ALM-` | Common ground for optocouplers |

#### B. Modbus Telemetry (5-Pin Debug Connector Cable)
Connect the female 5-pin debug cable from the iSV57 tuning port to the PCB RS232 port:
| ControlBoard Pin | iSV57 5-Pin Debug Port | Wire Function |
| :--- | :---: | :--- |
| **TX** | Pin 2 (RX) | ESP32 transmits Modbus queries to servo |
| **RX** | Pin 3 (TX) | ESP32 receives encoder & current telemetry |
| **GND** | Pin 1 (GND) | Logic ground reference |

---

## ⚡ Pre-Power Check & Verification

Before plugging into your 36V / 48V power supply:

1. 🔍 **Visual Inspection:** Inspect all solder joints under good light for solder bridges, particularly across adjacent pins of the SP2323/SP3232 IC.
2. ⚡ **Continuity & Short-Circuit Test:** With a multimeter in continuity/resistance mode:
   * Verify there is **NO short circuit** between `+` and `-` on the XT30 power connector.
   * Verify `GND` is properly continuous between the XT30 input, ESP32 ground pins, and the servo common ground.
3. 🔌 **USB Flashing Test:** Plug in the ESP32 via USB-C *without* high-voltage PSU connected. The board should enumerate as a serial COM port in Windows Device Manager.
4. 💡 **RGB LED Verification:** Once flashed, the on-board WS2812 LED will display the current system state (see [RGB Status LED Guide](LED.md)).
